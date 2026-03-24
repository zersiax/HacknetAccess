using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for MissionListingServer — faction mission boards
    /// (Entropy, CSEC, extension factions) and public article boards
    /// (Slashbot, NetEdu, Kellis Biotech).
    /// States: 0=NeedLogin, 1=Board, 2=Message, 3=Login.
    /// Public boards start at state 1 (no login).
    /// Item types: contracts (missionAssigner), articles (isPublic), posts (other).
    /// </summary>
    [HarmonyPatch]
    internal static class MissionListingPatches
    {
        private static int _lastListingState = -1;
        private static int _missionIndex;
        private static int _missionCount;
        private static bool _listingActive;
        private static int _pendingButton = -1;
        private static List<string> _missionTitles = new List<string>();
        private static List<int> _missionGameIndices = new List<int>();
        private static object _listingInstance;

        // Item type flags — determined from instance fields
        private static bool _isPublic;
        private static bool _isMissionAssigner;
        private static string _listingTitle;

        // Login screen state — known credentials navigation
        private static List<string> _knownUserNames = new List<string>();
        private static List<string> _knownUserPasses = new List<string>();
        private static int _loginUserIndex;
        private static bool _loginAnnouncedUsers;
        private static int _loginResult = -1; // -1=pending, 0=failed, 1=success

        /// <summary>
        /// Whether the mission listing daemon is currently being drawn
        /// or in a login transition. Used by DisplayModulePatches to
        /// detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _listingActive || _loginInProgress;
        private static bool _loginInProgress;

        /// <summary>
        /// Prefix on MissionListingServer.draw — mark listing as active.
        /// </summary>
        [HarmonyPatch]
        static class ListingDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MissionListingServer"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _listingActive = true;
                _listingInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on MissionListingServer.draw — detect state changes.
        /// </summary>
        [HarmonyPatch]
        static class ListingDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MissionListingServer"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    int stateVal = (int)AccessTools.Field(type, "state").GetValue(__instance);

                    if (stateVal != _lastListingState)
                    {
                        int prevState = _lastListingState;
                        _lastListingState = stateVal;

                        // Read item type flags on first state detection
                        ReadItemType(__instance);

                        switch (stateVal)
                        {
                            case 0: // NeedLogin
                                Plugin.Announce(Loc.Get("listing.needLogin",
                                    _listingTitle ?? "Mission board"), false);
                                break;

                            case 1: // Board
                                _loginInProgress = false;
                                TerminalPatches.SuppressPromptAnnounce = false;
                                // Returning from login — clear terminal, ensure display stays on daemon
                                if (prevState == 3)
                                {
                                    ClearTerminalLine();
                                    EnsureDisplayCommand(__instance);
                                    Plugin.Announce(Loc.Get("daemon.loginSuccess"), false);
                                }

                                BuildMissionList(__instance);
                                if (_missionCount > 0)
                                {
                                    if (prevState == 2)
                                    {
                                        // Returning from detail — keep index
                                        AnnounceCurrentItem();
                                    }
                                    else
                                    {
                                        _missionIndex = 0;
                                        AnnounceBoardHeader();
                                        AnnounceCurrentItem();
                                    }
                                }
                                else
                                {
                                    AnnounceBoardHeader();
                                }
                                break;

                            case 2: // Message (detail view)
                                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                                    "State changed to 2 (detail), announcing...");
                                AnnounceDetail(__instance);
                                break;

                            case 3: // Login
                                BuildKnownUserList(__instance);
                                if (!_loginAnnouncedUsers)
                                {
                                    _loginAnnouncedUsers = true;
                                    _loginUserIndex = 0;
                                    AnnounceLoginScreen();
                                }
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation in sub-states
                    if (_lastListingState == 2 || _lastListingState == 3)
                        DisplayModulePatches.DaemonClaimsEscape = true;

                    // Detect login result in state 3 by checking displayCache
                    if (_lastListingState == 3)
                        DetectLoginResult(__instance);

                    // Clear stale pending button if it wasn't consumed this frame
                    if (_pendingButton != -1)
                    {
                        DebugLogger.Log(LogCategory.Handler, "MissionListing",
                            $"Stale pending button {_pendingButton} cleared (not rendered by game)");
                        _pendingButton = -1;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "MissionListing",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Patch Button.doButton to handle listing button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class ListingButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "MissionListing",
                        $"Activated button: {myID}");
                }
            }
        }

        /// <summary>
        /// Read isPublic, missionAssigner, and listingTitle from the instance.
        /// </summary>
        private static void ReadItemType(object listing)
        {
            try
            {
                var type = listing.GetType();
                _isPublic = (bool)AccessTools.Field(type, "isPublic").GetValue(listing);
                _isMissionAssigner = (bool)AccessTools.Field(type, "missionAssigner")
                    .GetValue(listing);
                _listingTitle = (string)AccessTools.Field(type, "listingTitle")
                    ?.GetValue(listing) ?? "";
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"ItemType: public={_isPublic}, assigner={_isMissionAssigner}, title={_listingTitle}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"ReadItemType failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the board header with correct item count and type label.
        /// </summary>
        private static void AnnounceBoardHeader()
        {
            if (_isPublic || !_isMissionAssigner)
            {
                Plugin.Announce(Loc.Get("listing.boardArticle",
                    _listingTitle, _missionCount), false);
            }
            else
            {
                Plugin.Announce(Loc.Get("listing.boardMission",
                    _listingTitle, _missionCount), false);
            }
        }

        /// <summary>
        /// Build the mission list from the listing server's missions.
        /// Only includes items where hasListingFile() returns true,
        /// matching the game's draw loop filtering. Tracks each visible
        /// item's original index for correct button ID activation.
        /// </summary>
        private static void BuildMissionList(object listing)
        {
            _missionTitles.Clear();
            _missionGameIndices.Clear();
            _missionCount = 0;

            try
            {
                var type = listing.GetType();
                var missions = AccessTools.Field(type, "missions").GetValue(listing) as IList;
                if (missions == null) return;

                // Read missionFolder for hasListingFile check
                var missionFolder = AccessTools.Field(type, "missionFolder")?.GetValue(listing);
                IList folderFiles = null;
                FieldInfo fileNameField = null;
                if (missionFolder != null)
                {
                    folderFiles = AccessTools.Field(missionFolder.GetType(), "files")
                        ?.GetValue(missionFolder) as IList;
                    if (folderFiles != null && folderFiles.Count > 0)
                        fileNameField = AccessTools.Field(folderFiles[0].GetType(), "name");
                }

                for (int i = 0; i < missions.Count; i++)
                {
                    var mission = missions[i];
                    if (mission == null) continue;
                    var missionType = mission.GetType();

                    string postingTitle = (string)AccessTools.Field(missionType, "postingTitle")
                        ?.GetValue(mission);
                    if (string.IsNullOrEmpty(postingTitle))
                    {
                        postingTitle = _isPublic ? "Article" : "Contract";
                    }

                    // Replicate hasListingFile check: name with spaces→underscores
                    if (folderFiles != null && fileNameField != null)
                    {
                        string searchName = postingTitle.Replace(" ", "_");
                        bool found = false;
                        for (int f = 0; f < folderFiles.Count; f++)
                        {
                            string fileName = (string)fileNameField.GetValue(folderFiles[f]);
                            if (fileName == searchName)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            DebugLogger.Log(LogCategory.Handler, "MissionListing",
                                $"Skipping mission[{i}] '{postingTitle}' — no listing file");
                            continue;
                        }
                    }

                    _missionTitles.Add(postingTitle);
                    _missionGameIndices.Add(i);
                    _missionCount++;
                }

                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"BuildMissionList: {_missionCount} visible of {missions.Count} total");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"BuildMissionList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the currently selected item with type-appropriate label.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            if (_missionIndex < 0 || _missionIndex >= _missionCount) return;

            if (_isPublic || !_isMissionAssigner)
            {
                Plugin.Announce(Loc.Get("listing.itemArticle",
                    _missionIndex + 1, _missionCount, _missionTitles[_missionIndex]));
            }
            else
            {
                Plugin.Announce(Loc.Get("listing.itemMission",
                    _missionIndex + 1, _missionCount, _missionTitles[_missionIndex]));
            }
        }

        /// <summary>
        /// Announce the detail view (state 2).
        /// </summary>
        private static void AnnounceDetail(object listing)
        {
            try
            {
                var type = listing.GetType();
                int targetIndex = (int)AccessTools.Field(type, "targetIndex").GetValue(listing);
                var missions = AccessTools.Field(type, "missions").GetValue(listing) as IList;

                if (missions != null && targetIndex >= 0 && targetIndex < missions.Count)
                {
                    var mission = missions[targetIndex];
                    var missionType = mission.GetType();

                    string title = (string)AccessTools.Field(missionType, "postingTitle")
                        ?.GetValue(mission) ?? "Item";
                    string body = (string)AccessTools.Field(missionType, "postingBody")
                        ?.GetValue(mission) ?? "";

                    // Check accept conditions for assigner boards
                    string hint = "";
                    if (_isMissionAssigner)
                    {
                        var osType = AccessTools.TypeByName("Hacknet.OS");
                        var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                        object currentMission = os != null
                            ? AccessTools.Field(osType, "currentMission")?.GetValue(os) : null;

                        if (currentMission != null)
                        {
                            bool wasAutoGenerated = (bool)AccessTools.Field(
                                currentMission.GetType(), "wasAutoGenerated")
                                .GetValue(currentMission);
                            hint = Loc.Get("listing.hasActiveMission");
                            if (wasAutoGenerated)
                                hint += " " + Loc.Get("listing.abandonHint");
                        }
                        else
                        {
                            string groupName = (string)AccessTools.Field(type, "groupName")
                                ?.GetValue(listing) ?? "";
                            var currentFaction = AccessTools.Field(osType, "currentFaction")
                                ?.GetValue(os);
                            bool factionMatch = false;
                            if (currentFaction != null)
                            {
                                string factionId = (string)AccessTools.Field(
                                    currentFaction.GetType(), "idName")
                                    ?.GetValue(currentFaction) ?? "";
                                factionMatch = factionId.ToLower() == groupName.ToLower();
                            }

                            if (factionMatch)
                                hint = Loc.Get("listing.acceptHint");
                            else
                                hint = Loc.Get("listing.wrongFaction");
                        }
                    }

                    string announcement = Loc.Get("listing.detail", title, body);
                    if (!string.IsNullOrEmpty(hint))
                        announcement += " " + hint;
                    announcement += " " + Loc.Get("listing.back");
                    Plugin.Announce(announcement, false);
                }
                else
                {
                    DebugLogger.Log(LogCategory.Handler, "MissionListing",
                        $"AnnounceDetail: targetIndex={targetIndex}, missions={(missions?.Count ?? -1)}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"AnnounceDetail failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the terminal's current input line after login completes.
        /// Prevents password text from lingering on the command line.
        /// </summary>
        private static void ClearTerminalLine()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var terminal = AccessTools.Field(osType, "terminal")?.GetValue(os);
                if (terminal == null) return;

                AccessTools.Field(terminal.GetType(), "currentLine")
                    ?.SetValue(terminal, "");
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    "Cleared terminal currentLine after login");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"ClearTerminalLine failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process keyboard shortcuts when listing is active.
        /// Public/article boards: bare keys when display focused.
        /// Assigner/contract boards: Ctrl+keys always, bare keys when focused.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!_listingActive) return;
            _listingActive = false;

            bool ctrl = currentState.IsKeyDown(Keys.LeftControl)
                     || currentState.IsKeyDown(Keys.RightControl);
            bool focused = DisplayModulePatches.DisplayHasFocus;

            // Accept bare keys when display focused, Ctrl+keys as fallback
            bool enter = (focused && Plugin.IsKeyPressed(Keys.Enter, currentState))
                      || (ctrl && Plugin.IsKeyPressed(Keys.Enter, currentState));
            bool up = (focused && Plugin.IsKeyPressed(Keys.Up, currentState))
                   || (ctrl && Plugin.IsKeyPressed(Keys.Up, currentState));
            bool down = (focused && Plugin.IsKeyPressed(Keys.Down, currentState))
                     || (ctrl && Plugin.IsKeyPressed(Keys.Down, currentState));
            bool escape = Plugin.IsKeyPressed(Keys.Escape, currentState);

            switch (_lastListingState)
            {
                case 0: // NeedLogin
                    if (enter)
                    {
                        _loginAnnouncedUsers = false;
                        _loginResult = -1;
                        _loginInProgress = true;
                        TerminalPatches.SuppressPromptAnnounce = true;
                        SetStateDirectly(3, "startLogin");
                        // Stay in display focus — login is handled via known credentials
                    }
                    else if (escape && !focused)
                        SetDisplayCommand("connect");
                    break;

                case 1: // Board
                    if (up && _missionCount > 0)
                    {
                        if (_missionIndex > 0) _missionIndex--;
                        AnnounceCurrentItem();
                    }
                    else if (down && _missionCount > 0)
                    {
                        if (_missionIndex < _missionCount - 1) _missionIndex++;
                        AnnounceCurrentItem();
                    }
                    else if (enter && _missionCount > 0)
                    {
                        int gameIndex = _missionGameIndices[_missionIndex];
                        SetStateDirectly(2);
                        SetTargetIndex(gameIndex);
                        DebugLogger.Log(LogCategory.Handler, "MissionListing",
                            $"Enter: state→2, targetIndex={gameIndex} (navIndex={_missionIndex})");
                    }
                    else if (escape && !focused)
                        SetDisplayCommand("connect");
                    break;

                case 3: // Login
                    if (up && _knownUserNames.Count > 0)
                    {
                        if (_loginUserIndex > 0) _loginUserIndex--;
                        AnnounceCurrentUser();
                    }
                    else if (down && _knownUserNames.Count > 0)
                    {
                        if (_loginUserIndex < _knownUserNames.Count - 1) _loginUserIndex++;
                        AnnounceCurrentUser();
                    }
                    else if (enter && _knownUserNames.Count > 0 && _loginResult == -1)
                    {
                        TerminalPatches.SuppressPromptAnnounce = true;
                        ForceLoginWithUser(_loginUserIndex);
                    }
                    else if (enter && _loginResult == 0)
                    {
                        // Retry after failure
                        RetryLogin();
                    }
                    else if (escape)
                    {
                        // Back to NeedLogin state
                        CallLoginGoBack();
                    }
                    break;

                case 2: // Message (detail)
                    if (_isMissionAssigner && enter)
                    {
                        _pendingButton = 800005; // Accept (complex game logic, keep button)
                        DebugLogger.Log(LogCategory.Handler, "MissionListing",
                            "Accept pending: button 800005");
                    }
                    else if (_isMissionAssigner && ctrl
                        && Plugin.IsKeyPressed(Keys.A, currentState))
                        _pendingButton = 8000105; // Abandon
                    else if (escape)
                        SetStateDirectly(1);
                    break;
            }
        }

        /// <summary>
        /// Set the listing server's state field directly via reflection.
        /// Optionally calls a method (e.g. startLogin) before setting state.
        /// </summary>
        private static void SetStateDirectly(int newState, string methodToCall = null)
        {
            if (_listingInstance == null) return;
            try
            {
                var type = _listingInstance.GetType();
                if (methodToCall != null)
                {
                    AccessTools.Method(type, methodToCall)
                        ?.Invoke(_listingInstance, null);
                }
                AccessTools.Field(type, "state").SetValue(_listingInstance, newState);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"SetStateDirectly({newState}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Set the listing server's targetIndex field directly.
        /// </summary>
        private static void SetTargetIndex(int index)
        {
            if (_listingInstance == null) return;
            try
            {
                AccessTools.Field(_listingInstance.GetType(), "targetIndex")
                    .SetValue(_listingInstance, index);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"SetTargetIndex({index}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Set os.display.command to exit the daemon view.
        /// </summary>
        private static void SetDisplayCommand(string command)
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;
                var display = AccessTools.Field(osType, "display")?.GetValue(os);
                if (display == null) return;
                AccessTools.Field(display.GetType(), "command")?.SetValue(display, command);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"SetDisplayCommand({command}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Build the list of known credentials from comp.users.
        /// Filters by known=true and valid user type (0, 1, or 3).
        /// </summary>
        private static void BuildKnownUserList(object listing)
        {
            if (_knownUserNames.Count > 0) return; // Already built
            _knownUserNames.Clear();
            _knownUserPasses.Clear();
            try
            {
                var type = listing.GetType();
                var compField = AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "comp");
                var comp = compField?.GetValue(listing);
                if (comp == null) return;

                var users = AccessTools.Field(comp.GetType(), "users")
                    ?.GetValue(comp) as IList;
                if (users == null) return;

                for (int i = 0; i < users.Count; i++)
                {
                    var user = users[i];
                    var userType = user.GetType();
                    bool known = (bool)AccessTools.Field(userType, "known").GetValue(user);
                    byte acctType = (byte)AccessTools.Field(userType, "type").GetValue(user);

                    // validUser: type 0 (admin), 1 (normal), or 3
                    if (known && (acctType == 0 || acctType == 1 || acctType == 3))
                    {
                        string name = (string)AccessTools.Field(userType, "name").GetValue(user);
                        string pass = (string)AccessTools.Field(userType, "pass").GetValue(user);
                        _knownUserNames.Add(name);
                        _knownUserPasses.Add(pass);
                    }
                }
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"Known users: {_knownUserNames.Count}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"BuildKnownUserList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the login screen with known credentials.
        /// </summary>
        private static void AnnounceLoginScreen()
        {
            if (_knownUserNames.Count > 0)
            {
                Plugin.Announce(Loc.Get("login.knownUsers", _knownUserNames.Count), false);
                AnnounceCurrentUser();
            }
            else
            {
                Plugin.Announce(Loc.Get("login.noKnownUsers"), false);
            }
        }

        /// <summary>
        /// Announce the currently selected known user.
        /// </summary>
        private static void AnnounceCurrentUser()
        {
            if (_loginUserIndex < 0 || _loginUserIndex >= _knownUserNames.Count) return;
            Plugin.Announce(Loc.Get("login.user",
                _loginUserIndex + 1, _knownUserNames.Count,
                _knownUserNames[_loginUserIndex]));
        }

        /// <summary>
        /// Call forceLogin on the AuthenticatingDaemon with the selected credentials.
        /// </summary>
        private static void ForceLoginWithUser(int index)
        {
            if (_listingInstance == null || index < 0 || index >= _knownUserNames.Count) return;
            try
            {
                var method = AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"),
                    "forceLogin",
                    new[] { typeof(string), typeof(string) });
                method?.Invoke(_listingInstance,
                    new object[] { _knownUserNames[index], _knownUserPasses[index] });
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"ForceLogin called for user: {_knownUserNames[index]}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"ForceLogin failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect login result by checking displayCache format.
        /// displayCache has 4+ parts when login completes (part[3] is result code).
        /// </summary>
        private static void DetectLoginResult(object listing)
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                string cache = (string)AccessTools.Field(osType, "displayCache")?.GetValue(os);
                if (string.IsNullOrEmpty(cache)) return;

                string[] separator = new[] { "#$#$#$$#$&$#$#$#$#" };
                string[] parts = cache.Split(separator, StringSplitOptions.None);

                if (parts.Length > 3 && parts[0] == "loginData")
                {
                    int result = Convert.ToInt32(parts[3]);
                    if (result == 0 && _loginResult != 0)
                    {
                        _loginResult = 0;
                        Plugin.Announce(Loc.Get("login.failed"), false);
                    }
                    // Success (result > 0) is handled by userLoggedIn → state change to 1
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"DetectLoginResult failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Retry login after failure — re-calls startLogin to reset the flow.
        /// </summary>
        private static void RetryLogin()
        {
            if (_listingInstance == null) return;
            try
            {
                _loginResult = -1;
                _loginAnnouncedUsers = false;
                _knownUserNames.Clear();
                _knownUserPasses.Clear();
                var type = _listingInstance.GetType();
                AccessTools.Method(type, "startLogin")?.Invoke(_listingInstance, null);
                DebugLogger.Log(LogCategory.Handler, "MissionListing", "Login retry");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"RetryLogin failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure display.command is set to the daemon's name so the daemon keeps drawing.
        /// After forceLogin, terminal commands can change display.command away from the daemon.
        /// </summary>
        private static void EnsureDisplayCommand(object listing)
        {
            try
            {
                var daemonType = AccessTools.TypeByName("Hacknet.Daemon");
                string daemonName = (string)AccessTools.Field(daemonType, "name")
                    ?.GetValue(listing);
                if (string.IsNullOrEmpty(daemonName)) return;

                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var display = AccessTools.Field(osType, "display")?.GetValue(os);
                if (display == null) return;

                AccessTools.Field(display.GetType(), "command")?.SetValue(display, daemonName);
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"Ensured display.command = {daemonName}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"EnsureDisplayCommand failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Call loginGoBack to return to the NeedLogin state.
        /// </summary>
        private static void CallLoginGoBack()
        {
            if (_listingInstance == null) return;
            try
            {
                var method = AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"), "loginGoBack");
                method?.Invoke(_listingInstance, null);
                _knownUserNames.Clear();
                _knownUserPasses.Clear();
                _loginAnnouncedUsers = false;
                _loginResult = -1;
                _loginInProgress = false;
                TerminalPatches.SuppressPromptAnnounce = false;
                DebugLogger.Log(LogCategory.Handler, "MissionListing", "Login go back");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionListing",
                    $"CallLoginGoBack failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state when leaving listing context.
        /// </summary>
        public static void Reset()
        {
            _lastListingState = -1;
            _missionIndex = 0;
            _missionCount = 0;
            _listingActive = false;
            _pendingButton = -1;
            _listingInstance = null;
            _missionGameIndices.Clear();
            _isPublic = false;
            _isMissionAssigner = false;
            _listingTitle = null;
            _knownUserNames.Clear();
            _knownUserPasses.Clear();
            _loginUserIndex = 0;
            _loginAnnouncedUsers = false;
            _loginResult = -1;
            _loginInProgress = false;
        }
    }
}
