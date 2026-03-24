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
    /// Patches for MissionHubServer — the primary mission source (CSEC hub).
    /// 7-state state machine with pagination and keyboard navigation.
    /// Ctrl+Up/Down navigates missions, Ctrl+Enter accepts, Ctrl+Left/Right pages.
    /// </summary>
    [HarmonyPatch]
    internal static class MissionHubPatches
    {
        private static int _lastHubState = -1;
        private static int _missionIndex;
        private static int _missionCount;
        private static bool _hubActive;
        private static int _pendingButton = -1;
        private static List<string> _missionTitles = new List<string>();
        private static List<string> _missionKeys = new List<string>();
        private static object _hubInstance;

        // Login screen state — known credentials navigation
        private static List<string> _knownUserNames = new List<string>();
        private static List<string> _knownUserPasses = new List<string>();
        private static int _loginUserIndex;
        private static bool _loginAnnouncedUsers;
        private static int _loginResult = -1; // -1=pending, 0=failed, 1=success

        /// <summary>
        /// Whether the mission hub daemon is currently being drawn.
        /// Used by DisplayModulePatches to detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _hubActive || _loginInProgress;
        private static bool _loginInProgress;

        /// <summary>
        /// Prefix on MissionHubServer.draw — mark hub as active.
        /// </summary>
        [HarmonyPatch]
        static class HubDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MissionHubServer"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _hubActive = true;
                _hubInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on MissionHubServer.draw — detect state changes and announce.
        /// </summary>
        [HarmonyPatch]
        static class HubDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MissionHubServer"),
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

                    if (stateVal != _lastHubState)
                    {
                        int prevState = _lastHubState;
                        _lastHubState = stateVal;

                        switch (stateVal)
                        {
                            case 0: // Welcome
                                string groupName = (string)AccessTools.Field(type, "groupName")
                                    .GetValue(__instance);
                                Plugin.Announce(Loc.Get("hub.welcome", groupName), false);
                                break;

                            case 1: // Menu
                                _loginInProgress = false;
                                TerminalPatches.SuppressPromptAnnounce = false;
                                // Returning from login — clear login state, keep display on daemon
                                if (prevState == 2)
                                {
                                    ClearTerminalLine();
                                    EnsureDisplayCommand(__instance);
                                    Plugin.Announce(Loc.Get("daemon.loginSuccess"), false);
                                }
                                Plugin.Announce(Loc.Get("hub.menu"), false);
                                break;

                            case 2: // Login
                                BuildKnownUserList(__instance);
                                if (!_loginAnnouncedUsers)
                                {
                                    _loginAnnouncedUsers = true;
                                    _loginUserIndex = 0;
                                    AnnounceLoginScreen();
                                }
                                break;

                            case 3: // Listing
                                BuildMissionList(__instance);
                                if (_missionCount > 0)
                                {
                                    if (prevState == 4)
                                    {
                                        // Returning from preview, keep index
                                        AnnounceCurrentMission();
                                    }
                                    else
                                    {
                                        _missionIndex = 0;
                                        Plugin.Announce(Loc.Get("hub.listing", _missionCount), false);
                                        AnnounceCurrentMission();
                                    }
                                }
                                else
                                {
                                    Plugin.Announce(Loc.Get("hub.noMissions"), false);
                                }
                                break;

                            case 4: // ContractPreview
                                AnnounceContractPreview(__instance);
                                break;

                            case 6: // CancelContract
                                Plugin.Announce(Loc.Get("hub.cancel"), false);
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation in sub-states
                    if (_lastHubState == 2 || _lastHubState == 3
                        || _lastHubState == 4 || _lastHubState == 6)
                        DisplayModulePatches.DaemonClaimsEscape = true;

                    // Detect login result in state 2 by checking displayCache
                    if (_lastHubState == 2)
                        DetectLoginResult();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "MissionHub",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch Button.doButton to handle hub button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class HubButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "MissionHub",
                        $"Activated button: {myID}");
                }
            }
        }

        /// <summary>
        /// Build the mission list from hub's listing data.
        /// </summary>
        private static void BuildMissionList(object hub)
        {
            _missionTitles.Clear();
            _missionKeys.Clear();
            _missionCount = 0;

            try
            {
                var type = hub.GetType();
                var listingsFolder = AccessTools.Field(type, "listingsFolder").GetValue(hub);
                if (listingsFolder == null) return;

                var files = AccessTools.Field(listingsFolder.GetType(), "files")
                    .GetValue(listingsFolder) as IList;
                if (files == null) return;

                var listingMissions = AccessTools.Field(type, "listingMissions")
                    .GetValue(hub) as IDictionary;
                if (listingMissions == null) return;

                foreach (var file in files)
                {
                    string fileName = (string)AccessTools.Field(file.GetType(), "name")
                        .GetValue(file);
                    if (fileName == null) continue;

                    // Key is file name without extension
                    string key = fileName.Replace(".txt", "");

                    if (listingMissions.Contains(key))
                    {
                        var mission = listingMissions[key];
                        if (mission == null) continue;

                        var missionType = mission.GetType();
                        string postingTitle = (string)AccessTools.Field(missionType, "postingTitle")
                            ?.GetValue(mission);
                        if (string.IsNullOrEmpty(postingTitle))
                        {
                            postingTitle = key;
                        }

                        _missionTitles.Add(postingTitle);
                        _missionKeys.Add(key);
                        _missionCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"BuildMissionList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the currently selected mission in the list.
        /// </summary>
        private static void AnnounceCurrentMission()
        {
            if (_missionIndex < 0 || _missionIndex >= _missionCount) return;
            Plugin.Announce(Loc.Get("hub.mission",
                _missionIndex + 1, _missionCount, _missionTitles[_missionIndex]));
        }

        /// <summary>
        /// Announce the contract preview details.
        /// </summary>
        private static void AnnounceContractPreview(object hub)
        {
            try
            {
                var type = hub.GetType();
                var listingMissions = AccessTools.Field(type, "listingMissions")
                    .GetValue(hub) as IDictionary;
                int selectedIndex = (int)AccessTools.Field(type, "selectedElementIndex")
                    .GetValue(hub);

                if (_missionKeys.Count > selectedIndex && selectedIndex >= 0)
                {
                    string key = _missionKeys[selectedIndex];
                    if (listingMissions != null && listingMissions.Contains(key))
                    {
                        var mission = listingMissions[key];
                        var missionType = mission.GetType();

                        string title = (string)AccessTools.Field(missionType, "postingTitle")
                            ?.GetValue(mission) ?? "Unknown";
                        string body = (string)AccessTools.Field(missionType, "postingBody")
                            ?.GetValue(mission) ?? "";

                        Plugin.Announce(Loc.Get("hub.preview", title, body), false);
                        return;
                    }
                }

                // Fallback: try to read from the selected key directly
                Plugin.Announce(Loc.Get("hub.preview", "Contract", ""), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"AnnounceContractPreview failed: {ex.Message}");
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
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    "Cleared terminal currentLine after login");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"ClearTerminalLine failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process keyboard shortcuts when hub is active.
        /// Supports bare keys when display has focus, Ctrl+keys always.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!_hubActive) return;
            _hubActive = false;

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
            bool left = (focused && Plugin.IsKeyPressed(Keys.Left, currentState))
                     || (ctrl && Plugin.IsKeyPressed(Keys.Left, currentState));
            bool right = (focused && Plugin.IsKeyPressed(Keys.Right, currentState))
                      || (ctrl && Plugin.IsKeyPressed(Keys.Right, currentState));

            switch (_lastHubState)
            {
                case 0: // Welcome
                    if (enter)
                    {
                        _loginAnnouncedUsers = false;
                        _loginResult = -1;
                        _loginInProgress = true;
                        TerminalPatches.SuppressPromptAnnounce = true;
                        _knownUserNames.Clear();
                        _knownUserPasses.Clear();
                        CallStartLogin();
                        // Stay in display focus — login is handled via known credentials
                    }
                    else if (escape && !focused)
                        _pendingButton = 12010; // Exit
                    break;

                case 2: // Login
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
                        RetryLogin();
                    }
                    else if (escape)
                    {
                        CallLoginGoBack();
                    }
                    break;

                case 1: // Menu
                    if (enter)
                        _pendingButton = 101010; // Contract Listing
                    else if (ctrl && Plugin.IsKeyPressed(Keys.U, currentState))
                        _pendingButton = 101015; // User List
                    else if (ctrl && Plugin.IsKeyPressed(Keys.A, currentState))
                        _pendingButton = 101017; // Abort Contract
                    else if (escape && !focused)
                        _pendingButton = 102015; // Exit
                    break;

                case 3: // Listing
                    if (up && _missionCount > 0)
                    {
                        if (_missionIndex > 0) _missionIndex--;
                        AnnounceCurrentMission();
                    }
                    else if (down && _missionCount > 0)
                    {
                        if (_missionIndex < _missionCount - 1) _missionIndex++;
                        AnnounceCurrentMission();
                    }
                    else if (enter && _missionCount > 0)
                    {
                        int buttonId = _missionIndex * 139284 + 984275 + _missionIndex;
                        _pendingButton = buttonId;
                    }
                    else if (right)
                        _pendingButton = 188278102; // Next page
                    else if (left)
                        _pendingButton = 188278101; // Previous page
                    else if (escape)
                        _pendingButton = 974748322; // Back
                    break;

                case 4: // ContractPreview
                    if (enter)
                        _pendingButton = 2171615; // Accept
                    else if (escape)
                        _pendingButton = 2171618; // Back
                    break;

                case 6: // CancelContract
                    if (enter)
                        _pendingButton = 142011; // Abandon
                    else if (escape)
                        _pendingButton = 142015; // Back
                    break;
            }
        }

        /// <summary>
        /// Call startLogin on the AuthenticatingDaemon and set state to Login (2).
        /// </summary>
        private static void CallStartLogin()
        {
            if (_hubInstance == null) return;
            try
            {
                var type = _hubInstance.GetType();
                AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"), "startLogin")
                    ?.Invoke(_hubInstance, null);
                AccessTools.Field(type, "state").SetValue(_hubInstance, 2); // HubState.Login
                DebugLogger.Log(LogCategory.Handler, "MissionHub", "Called startLogin");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"CallStartLogin failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Build the list of known credentials from comp.users.
        /// </summary>
        private static void BuildKnownUserList(object hub)
        {
            if (_knownUserNames.Count > 0) return;
            _knownUserNames.Clear();
            _knownUserPasses.Clear();
            try
            {
                var compField = AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "comp");
                var comp = compField?.GetValue(hub);
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

                    if (known && (acctType == 0 || acctType == 1 || acctType == 3))
                    {
                        string name = (string)AccessTools.Field(userType, "name").GetValue(user);
                        string pass = (string)AccessTools.Field(userType, "pass").GetValue(user);
                        _knownUserNames.Add(name);
                        _knownUserPasses.Add(pass);
                    }
                }
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"Known users: {_knownUserNames.Count}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
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
        /// Call forceLogin with the selected credentials.
        /// </summary>
        private static void ForceLoginWithUser(int index)
        {
            if (_hubInstance == null || index < 0 || index >= _knownUserNames.Count) return;
            try
            {
                var method = AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"),
                    "forceLogin",
                    new[] { typeof(string), typeof(string) });
                method?.Invoke(_hubInstance,
                    new object[] { _knownUserNames[index], _knownUserPasses[index] });
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"ForceLogin called for user: {_knownUserNames[index]}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"ForceLogin failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect login result by checking displayCache format.
        /// </summary>
        private static void DetectLoginResult()
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
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"DetectLoginResult failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Retry login after failure.
        /// </summary>
        private static void RetryLogin()
        {
            if (_hubInstance == null) return;
            try
            {
                _loginResult = -1;
                _loginAnnouncedUsers = false;
                _knownUserNames.Clear();
                _knownUserPasses.Clear();
                AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"), "startLogin")
                    ?.Invoke(_hubInstance, null);
                DebugLogger.Log(LogCategory.Handler, "MissionHub", "Login retry");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"RetryLogin failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure display.command is set to the daemon's name after login.
        /// </summary>
        private static void EnsureDisplayCommand(object hub)
        {
            try
            {
                var daemonType = AccessTools.TypeByName("Hacknet.Daemon");
                string daemonName = (string)AccessTools.Field(daemonType, "name")
                    ?.GetValue(hub);
                if (string.IsNullOrEmpty(daemonName)) return;

                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var display = AccessTools.Field(osType, "display")?.GetValue(os);
                if (display == null) return;

                AccessTools.Field(display.GetType(), "command")?.SetValue(display, daemonName);
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"Ensured display.command = {daemonName}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"EnsureDisplayCommand failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Call loginGoBack to return to the Welcome state.
        /// </summary>
        private static void CallLoginGoBack()
        {
            if (_hubInstance == null) return;
            try
            {
                AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AuthenticatingDaemon"), "loginGoBack")
                    ?.Invoke(_hubInstance, null);
                _knownUserNames.Clear();
                _knownUserPasses.Clear();
                _loginAnnouncedUsers = false;
                _loginResult = -1;
                _loginInProgress = false;
                TerminalPatches.SuppressPromptAnnounce = false;
                DebugLogger.Log(LogCategory.Handler, "MissionHub", "Login go back");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MissionHub",
                    $"CallLoginGoBack failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state when leaving hub context.
        /// </summary>
        public static void Reset()
        {
            _lastHubState = -1;
            _missionIndex = 0;
            _missionCount = 0;
            _hubActive = false;
            _pendingButton = -1;
            _hubInstance = null;
            _knownUserNames.Clear();
            _knownUserPasses.Clear();
            _loginUserIndex = 0;
            _loginAnnouncedUsers = false;
            _loginResult = -1;
            _loginInProgress = false;
        }
    }
}
