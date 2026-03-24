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
    /// Patches for the display module — ls, cat, connect, probe results.
    /// Up/Down arrows navigate lines in cat view.
    /// F5 toggles display focus for daemon interaction (bare Up/Down/Enter/Escape).
    /// When no daemon is active, F5 re-reads current display content.
    /// </summary>
    [HarmonyPatch]
    internal static class DisplayModulePatches
    {
        private static string _lastDisplayMode;
        private static string _lastDisplayContent;
        private static string[] _catLines;
        private static string _lastLsContent;
        private static List<string> _lsItems = new List<string>();
        private static KeyboardState _prevKeyState;

        /// <summary>
        /// When true, display focus will be restored on the next ProcessInput call.
        /// Set during Draw phase by RestoreDisplayFocus, applied during Update phase
        /// to avoid timing issues with the auto-exit check.
        /// </summary>
        private static bool _pendingFocusRestore;

        /// <summary>
        /// Whether the display panel currently has keyboard focus.
        /// When true, bare Up/Down/Enter/Escape are routed to daemon patches
        /// instead of the terminal.
        /// </summary>
        public static bool DisplayHasFocus { get; set; }

        /// <summary>
        /// Set by daemon draw Postfixes when the daemon is in a sub-state where
        /// Escape should navigate back within the daemon (e.g., detail→list)
        /// rather than exiting display focus.
        /// </summary>
        public static bool DaemonClaimsEscape { get; set; }

        /// <summary>
        /// After doCommandModule, detect display mode changes.
        /// </summary>
        [HarmonyPatch]
        static class CommandModulePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DisplayModule"), "doCommandModule");
            }

            static void Postfix(object __instance)
            {
                string command = (string)AccessTools.Field(
                    __instance.GetType(), "command")?.GetValue(__instance);
                if (command == null || command == _lastDisplayMode) return;

                _lastDisplayMode = command;
                DebugLogger.Log(LogCategory.Handler, "Display", $"Mode: {command}");
            }
        }

        /// <summary>
        /// After doCatDisplay, announce file content and enable line navigation.
        /// </summary>
        [HarmonyPatch]
        static class CatDisplayPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DisplayModule"), "doCatDisplay");
            }

            static void Postfix(object __instance)
            {
                string content = GetOSDisplayCache(__instance);
                if (content == null || content == _lastDisplayContent) return;

                _lastDisplayContent = content;

                // Split into lines for F5 re-read
                _catLines = content.Split(new[] { '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries);

                // Announce full content
                string clean = content.Replace("\r\n", " ").Replace("\n", " ");
                Plugin.Announce(Loc.Get("display.cat", clean), false);
            }
        }

        /// <summary>
        /// After doLsDisplay, build list of folders/files for navigation.
        /// </summary>
        [HarmonyPatch]
        static class LsDisplayPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DisplayModule"), "doLsDisplay");
            }

            static void Postfix(object __instance)
            {
                string lsContent = BuildLsContent(__instance);
                if (lsContent == null || lsContent == _lastLsContent) return;

                _lastLsContent = lsContent;

                // Add each item to terminal review buffer so Ctrl+Up/Down can navigate them
                foreach (string item in _lsItems)
                {
                    TerminalPatches.TrackOutput(item);
                }

                Plugin.Announce(lsContent, false);
            }
        }

        /// <summary>
        /// After doConnectDisplay, announce connection info.
        /// </summary>
        [HarmonyPatch]
        static class ConnectDisplayPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DisplayModule"), "doConnectDisplay");
            }

            static void Postfix(object __instance)
            {
                string content = GetConnectInfo();
                if (content == null || content == _lastDisplayContent) return;

                _lastDisplayContent = content;
                Plugin.Announce(content, false);
            }
        }

        /// <summary>
        /// After doProbeDisplay, announce port scan results.
        /// </summary>
        [HarmonyPatch]
        static class ProbeDisplayPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DisplayModule"), "doProbeDisplay");
            }

            static void Postfix(object __instance)
            {
                string content = GetProbeInfo();
                if (content == null || content == _lastDisplayContent) return;

                _lastDisplayContent = content;
                Plugin.Announce(content, false);
            }
        }

        /// <summary>
        /// Read os.displayCache (file content for cat).
        /// </summary>
        private static string GetOSDisplayCache(object displayModule)
        {
            try
            {
                var osField = AccessTools.Field(displayModule.GetType(), "os");
                var os = osField?.GetValue(displayModule);
                if (os == null) return null;
                return (string)AccessTools.Field(os.GetType(), "displayCache")?.GetValue(os);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Build a text listing of current directory (folders + files).
        /// </summary>
        private static string BuildLsContent(object displayModule)
        {
            try
            {
                var osField = AccessTools.Field(displayModule.GetType(), "os");
                var os = osField?.GetValue(displayModule);
                if (os == null) return null;

                var osType = os.GetType();
                var connectedComp = AccessTools.Field(osType, "connectedComp")?.GetValue(os);
                var thisComputer = AccessTools.Field(osType, "thisComputer")?.GetValue(os);
                var computer = connectedComp ?? thisComputer;
                if (computer == null) return null;

                // Navigate to current directory
                var navPath = AccessTools.Field(osType, "navigationPath")
                    ?.GetValue(os) as IList;
                var files = AccessTools.Field(computer.GetType(), "files")?.GetValue(computer);
                var root = AccessTools.Field(files?.GetType(), "root")?.GetValue(files);
                var currentFolder = root;

                if (navPath != null && currentFolder != null)
                {
                    foreach (int idx in navPath)
                    {
                        var folders = AccessTools.Field(currentFolder.GetType(), "folders")
                            ?.GetValue(currentFolder) as IList;
                        if (folders != null && idx >= 0 && idx < folders.Count)
                        {
                            currentFolder = folders[idx];
                        }
                    }
                }

                if (currentFolder == null) return null;

                _lsItems.Clear();
                var sb = new StringBuilder();

                // Folders
                var folderList = AccessTools.Field(currentFolder.GetType(), "folders")
                    ?.GetValue(currentFolder) as IList;
                if (folderList != null)
                {
                    foreach (var folder in folderList)
                    {
                        string name = AccessTools.Field(folder.GetType(), "name")
                            ?.GetValue(folder) as string;
                        if (name != null)
                        {
                            _lsItems.Add("/" + name);
                            sb.AppendLine("/" + name);
                        }
                    }
                }

                // Files
                var fileList = AccessTools.Field(currentFolder.GetType(), "files")
                    ?.GetValue(currentFolder) as IList;
                if (fileList != null)
                {
                    foreach (var file in fileList)
                    {
                        string name = AccessTools.Field(file.GetType(), "name")
                            ?.GetValue(file) as string;
                        if (name != null)
                        {
                            _lsItems.Add(name);
                            sb.AppendLine(name);
                        }
                    }
                }

                if (_lsItems.Count == 0)
                    return "Empty directory.";

                return $"{_lsItems.Count} items. {sb.ToString().Trim()}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Display",
                    $"BuildLsContent failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get connection info string.
        /// </summary>
        private static string GetConnectInfo()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return null;

                var comp = AccessTools.Field(osType, "connectedComp")?.GetValue(os);
                if (comp == null)
                {
                    comp = AccessTools.Field(osType, "thisComputer")?.GetValue(os);
                }
                if (comp == null) return null;

                string name = AccessTools.Field(comp.GetType(), "name")?.GetValue(comp) as string;
                string ip = AccessTools.Field(comp.GetType(), "ip")?.GetValue(comp) as string;
                return $"Connected to {name} at {ip}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get probe/port scan info string.
        /// </summary>
        private static string GetProbeInfo()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return null;

                var comp = AccessTools.Field(osType, "connectedComp")?.GetValue(os)
                    ?? AccessTools.Field(osType, "thisComputer")?.GetValue(os);
                if (comp == null) return null;

                string name = AccessTools.Field(comp.GetType(), "name")?.GetValue(comp) as string;
                string ip = AccessTools.Field(comp.GetType(), "ip")?.GetValue(comp) as string;
                int portsNeeded = (int)(AccessTools.Field(comp.GetType(), "portsNeededForCrack")
                    ?.GetValue(comp) ?? 0);

                var ports = AccessTools.Field(comp.GetType(), "ports")
                    ?.GetValue(comp) as IList;

                var sb = new StringBuilder();
                sb.Append($"{name} at {ip}. ");
                sb.Append($"Ports needed to crack: {portsNeeded}. ");

                if (ports != null)
                {
                    int openCount = 0;
                    foreach (var port in ports)
                    {
                        // Port is a byte, 0 = closed, anything else = open cracked
                    }
                    sb.Append($"{ports.Count} ports total.");
                }

                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }


        /// <summary>
        /// Check if any interactive daemon is currently being drawn.
        /// Uses IsActive properties from daemon patches (set during previous frame's draw).
        /// </summary>
        private static bool IsInteractiveDaemonActive()
        {
            return MissionListingPatches.IsActive
                || MissionHubPatches.IsActive
                || MessageBoardPatches.IsActive
                || DatabaseDaemonPatches.IsActive;
        }

        /// <summary>
        /// Try to switch the display to an interactive daemon on the connected computer.
        /// This is needed because typing any command (ls, cat, etc.) switches
        /// the display away from the daemon, and there's no way to get back.
        /// Replicates what the game does on connect: calls navigatedTo()
        /// and sets display.command to the daemon's name.
        /// </summary>
        private static bool TrySwitchToDaemon()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return false;

                var connComp = AccessTools.Field(osType, "connectedComp")?.GetValue(os);
                if (connComp == null) return false;

                var daemons = AccessTools.Field(connComp.GetType(), "daemons")
                    ?.GetValue(connComp) as IList;
                if (daemons == null || daemons.Count == 0) return false;

                // Find the last daemon (same as the game's connect logic)
                var daemon = daemons[daemons.Count - 1];
                string daemonName = AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "name")
                    ?.GetValue(daemon) as string;
                if (string.IsNullOrEmpty(daemonName)) return false;

                // Call navigatedTo() to reset daemon state
                AccessTools.Method(daemon.GetType(), "navigatedTo")?.Invoke(daemon, null);

                // Set display command to daemon name (makes doDaemonDisplay draw it)
                var display = AccessTools.Field(osType, "display")?.GetValue(os);
                AccessTools.Field(display.GetType(), "command")?.SetValue(display, daemonName);

                DebugLogger.Log(LogCategory.Handler, "Display",
                    $"Switched display to daemon: {daemonName}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Display",
                    $"TrySwitchToDaemon failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// When not connected to any remote host, clear all cached daemon
        /// and display state. Daemon patches repopulate naturally when
        /// their draw methods fire on the next connection.
        /// </summary>
        private static void ResetIfDisconnected()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var connComp = AccessTools.Field(osType, "connectedComp")?.GetValue(os);
                if (connComp != null) return;

                // Not connected — clear everything
                if (_lastDisplayContent == null && _lastLsContent == null
                    && _lastDisplayMode == null) return; // Already clean

                MissionListingPatches.Reset();
                MissionHubPatches.Reset();
                MessageBoardPatches.Reset();
                DatabaseDaemonPatches.Reset();
                _lastDisplayContent = null;
                _lastLsContent = null;
                _lastDisplayMode = null;
                _catLines = null;
                _lsItems.Clear();
                DebugLogger.Log(LogCategory.Handler, "Display",
                    "Disconnected — cleared daemon and display state");
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Display",
                    $"ResetIfDisconnected failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Exit display focus and return to terminal.
        /// Called by daemon patches when login is triggered (terminal needs input).
        /// </summary>
        public static void ExitDisplayFocus()
        {
            if (!DisplayHasFocus) return;
            DisplayHasFocus = false;
            Plugin.Announce(Loc.Get("display.terminalFocused"), false);
            DebugLogger.Log(LogCategory.Handler, "Display", "Exited focus (login triggered)");
        }

        /// <summary>
        /// Request display focus restore after login completes.
        /// Sets a deferred flag that ProcessInput picks up during the Update phase,
        /// ensuring focus is set at the right time relative to the auto-exit check.
        /// Unlike EnterDisplayFocus(), does not announce "Display focused"
        /// because the daemon's own state announcement is sufficient.
        /// </summary>
        public static void RestoreDisplayFocus()
        {
            _pendingFocusRestore = true;
            DebugLogger.Log(LogCategory.Handler, "Display", "Focus restore requested (login complete)");
        }

        /// <summary>
        /// Enter display focus mode — bare Up/Down/Enter/Escape go to daemon.
        /// </summary>
        private static void EnterDisplayFocus()
        {
            DisplayHasFocus = true;

            // Exit network map focus if active
            if (NetworkMapPatches.HasFocus)
            {
                NetworkMapPatches.Reset();
            }

            Plugin.Announce(Loc.Get("display.focused"), false);
            DebugLogger.Log(LogCategory.Handler, "Display", "Entered display focus");
        }

        /// <summary>
        /// Re-read current display content based on mode.
        /// </summary>
        private static void ReReadDisplay()
        {
            if (_lastDisplayMode == "cat" && _catLines != null && _catLines.Length > 0)
            {
                string clean = _lastDisplayContent?.Replace("\r\n", " ").Replace("\n", " ") ?? "";
                Plugin.Announce(Loc.Get("display.cat", clean), false);
            }
            else if (_lastDisplayMode == "ls" && _lsItems.Count > 0)
            {
                Plugin.Announce(_lastLsContent ?? Loc.Get("display.empty"), false);
            }
            else if (_lastDisplayContent != null)
            {
                Plugin.Announce(_lastDisplayContent, false);
            }
            else
            {
                Plugin.Announce(Loc.Get("display.empty"), false);
            }
        }

        /// <summary>
        /// Process F5, display focus Escape, and arrow key navigation.
        /// Called from Plugin.ProcessInput().
        /// F5 when connected to a server with daemons: switches display to daemon
        /// and enters focus mode (bare Up/Down/Enter/Escape).
        /// F5 when already focused: re-reads display content.
        /// Escape exits focus mode back to terminal.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            var ks = currentState;

            // Clear stale state when not connected to any host
            ResetIfDisconnected();

            // Apply deferred focus restore (from login completion during Draw phase)
            if (_pendingFocusRestore)
            {
                _pendingFocusRestore = false;
                DisplayHasFocus = true;
                if (NetworkMapPatches.HasFocus)
                {
                    NetworkMapPatches.Reset();
                }
                DebugLogger.Log(LogCategory.Handler, "Display", "Focus restored (deferred)");
            }

            // Auto-exit focus if daemon stopped drawing (e.g., disconnected)
            if (DisplayHasFocus && !IsInteractiveDaemonActive())
            {
                DisplayHasFocus = false;
                MissionListingPatches.Reset();
                MissionHubPatches.Reset();
                MessageBoardPatches.Reset();
                DatabaseDaemonPatches.Reset();
                Plugin.Announce(Loc.Get("display.focusLost"), false);
                DebugLogger.Log(LogCategory.Handler, "Display", "Auto-exited focus (daemon gone)");
            }

            // Escape exits display focus, returns to terminal —
            // unless a daemon claims Escape for internal back-navigation
            if (DisplayHasFocus && Plugin.IsKeyPressed(Keys.Escape, ks))
            {
                if (DaemonClaimsEscape)
                {
                    DaemonClaimsEscape = false;
                    DebugLogger.Log(LogCategory.Handler, "Display",
                        "Escape claimed by daemon, staying in focus");
                }
                else
                {
                    DisplayHasFocus = false;
                    Plugin.Announce(Loc.Get("display.terminalFocused"));
                    DebugLogger.Log(LogCategory.Handler, "Display", "Exited display focus");
                    _prevKeyState = ks;
                    return;
                }
            }

            // F5: enter daemon focus, or re-read display
            if (Plugin.IsKeyPressed(Keys.F5, currentState))
            {
                DebugLogger.LogInput("F5", "Display focus/re-read");

                if (DisplayHasFocus)
                {
                    // Already focused — re-read display content
                    ReReadDisplay();
                }
                else if (IsInteractiveDaemonActive())
                {
                    // Daemon already showing — just enter focus mode
                    EnterDisplayFocus();
                }
                else if (TrySwitchToDaemon())
                {
                    // Switched display to daemon — enter focus mode
                    // Daemon will draw on next frame, patches will announce state
                    EnterDisplayFocus();
                }
                else
                {
                    // No daemon available — just re-read display
                    ReReadDisplay();
                }
                _prevKeyState = ks;
                return;
            }

            _prevKeyState = ks;
        }
    }
}
