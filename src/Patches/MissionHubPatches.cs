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

        /// <summary>
        /// Whether the mission hub daemon is currently being drawn.
        /// Used by DisplayModulePatches to detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _hubActive;

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
                                // Returning from login — restore display focus
                                if (prevState == 2)
                                {
                                    DisplayModulePatches.RestoreDisplayFocus();
                                    ClearTerminalLine();
                                    Plugin.Announce(Loc.Get("daemon.loginSuccess"), false);
                                }
                                Plugin.Announce(Loc.Get("hub.menu"), false);
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
                    if (_lastHubState == 3 || _lastHubState == 4 || _lastHubState == 6)
                        DisplayModulePatches.DaemonClaimsEscape = true;
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
                        _pendingButton = 11005; // Login
                        // Login uses terminal input — shift focus back
                        DisplayModulePatches.ExitDisplayFocus();
                    }
                    else if (escape && !focused)
                        _pendingButton = 12010; // Exit
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
        }
    }
}
