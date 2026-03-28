using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for executable modules: PortHackExe, ShellExe, TraceKillExe.
    /// Announces hack progress and completion to screen reader.
    /// Ctrl+O: overload proxy from all shells.
    /// Ctrl+T: set trap / trigger trap from all shells.
    /// </summary>
    [HarmonyPatch]
    internal static class ExeModulePatches
    {
        private static int _lastProgressThreshold;
        private static string _lastPorthackTarget;
        private static HashSet<int> _pendingShellButtons = new HashSet<int>();

        // Generic progress tracking per exe instance (keyed by PID)
        private static readonly Dictionary<int, int> _progressThresholds = new Dictionary<int, int>();

        // Track which shells have been announced as "trap ready"
        private static readonly HashSet<int> _trapReadyAnnounced = new HashSet<int>();
        private static int _lastTrapReadyCount;

        // Batch trap-triggered announcements (multiple shells fire on same frame)
        private static List<string> _trapTriggeredIPs = new List<string>();

        // Track opponent location for trap trigger alerts
        private static string _lastOpponentLocation = "";

        /// <summary>
        /// Postfix on PortHackExe.Update — announce progress at 25/50/75%.
        /// </summary>
        [HarmonyPatch]
        static class PorthackUpdatePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.PortHackExe"),
                    "Update", new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    float progress = (float)AccessTools.Field(type, "progress").GetValue(__instance);
                    string targetIP = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "targetIP")
                        .GetValue(__instance);

                    // Reset threshold tracking on new target
                    if (targetIP != _lastPorthackTarget)
                    {
                        _lastPorthackTarget = targetIP;
                        _lastProgressThreshold = 0;
                    }

                    int threshold = (int)(progress * 4); // 0,1,2,3,4
                    if (threshold > _lastProgressThreshold && threshold < 4)
                    {
                        _lastProgressThreshold = threshold;
                        int percent = threshold * 25;
                        Plugin.Announce(Loc.Get("exe.porthackProgress", percent), false);
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Porthack {percent}% on {targetIP}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"PorthackUpdate failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on PortHackExe.Completed — announce admin access gained.
        /// </summary>
        [HarmonyPatch]
        static class PorthackCompletedPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.PortHackExe"),
                    "Completed");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string targetIP = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "targetIP")
                        .GetValue(__instance);

                    Plugin.Announce(Loc.Get("exe.porthackComplete", targetIP));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Porthack complete on {targetIP}");

                    _lastProgressThreshold = 0;
                    _lastPorthackTarget = null;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"PorthackCompleted failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on ShellExe.LoadContent — announce shell opened.
        /// </summary>
        [HarmonyPatch]
        static class ShellLoadPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ShellExe"),
                    "LoadContent");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string targetIP = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "targetIP")
                        .GetValue(__instance);
                    Plugin.Announce(Loc.Get("exe.shellOpened", targetIP), false);
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Shell opened on {targetIP}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"ShellLoad failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on ShellExe.Update — detect trap loading→ready transition.
        /// Announces once per shell when ramCost reaches targetRamUse in trap state.
        /// </summary>
        [HarmonyPatch]
        static class ShellUpdatePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ShellExe"),
                    "Update", new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var shellType = __instance.GetType();
                    var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");
                    int state = (int)AccessTools.Field(shellType, "state").GetValue(__instance);
                    if (state != 2) return;

                    int ramCost = (int)AccessTools.Field(exeModType, "ramCost").GetValue(__instance);
                    int targetRam = (int)AccessTools.Field(shellType, "targetRamUse").GetValue(__instance);
                    if (ramCost != targetRam) return;

                    int hash = __instance.GetHashCode();
                    if (_trapReadyAnnounced.Contains(hash)) return;
                    _trapReadyAnnounced.Add(hash);

                    // Count total ready traps to batch the announcement
                    _lastTrapReadyCount++;

                    // Defer announcement by 1 frame to batch multiple shells
                    // (they often complete on the same frame)
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"ShellUpdate trap check failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on ShellExe.StartOverload — announce overload started.
        /// </summary>
        [HarmonyPatch]
        static class ShellOverloadStartPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ShellExe"),
                    "StartOverload");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string destIP = (string)AccessTools.Field(
                        __instance.GetType(), "destinationIP")
                        .GetValue(__instance);
                    Plugin.Announce(Loc.Get("exe.shellOverloadStart", destIP), false);
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Shell overload started on {destIP}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"ShellOverloadStart failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Prefix on ShellExe.completedAction — announce overload/trap completed.
        /// Must be Prefix because completedAction calls cancelTarget() which clears destinationIP.
        /// </summary>
        [HarmonyPatch]
        static class ShellActionCompletePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ShellExe"),
                    "completedAction", new[] { typeof(int) });
            }

            static void Prefix(int action, object __instance)
            {
                try
                {
                    // Clear trap-ready tracking so it can re-announce after next set
                    _trapReadyAnnounced.Remove(__instance.GetHashCode());

                    string destIP = (string)AccessTools.Field(
                        __instance.GetType(), "destinationIP")
                        .GetValue(__instance);

                    if (action == 1)
                    {
                        Plugin.Announce(Loc.Get("exe.shellOverloadDone", destIP));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Shell overload complete on {destIP}");
                    }
                    else if (action == 2)
                    {
                        // Batch: multiple shells trigger on the same frame
                        _trapTriggeredIPs.Add(destIP);
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Shell trap triggered on {destIP}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"ShellActionComplete failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on ShellExe.Completed — announce shell closed / port crack done.
        /// </summary>
        [HarmonyPatch]
        static class ShellCompletedPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ShellExe"),
                    "Completed");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string targetIP = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "targetIP")
                        .GetValue(__instance);

                    Plugin.Announce(Loc.Get("exe.shellComplete", targetIP));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Shell completed on {targetIP}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"ShellCompleted failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Postfix on TraceKillExe.Update — announce TraceKill status.
        /// No Completed() override needed; TraceKill just freezes trace timer.
        /// </summary>
        [HarmonyPatch]
        static class TraceKillUpdatePatch
        {
            private static bool _announced;

            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.TraceKillExe"),
                    "Update", new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    bool isExiting = (bool)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "isExiting")
                        .GetValue(__instance);

                    if (isExiting && !_announced)
                    {
                        _announced = true;
                        Plugin.Announce(Loc.Get("exe.traceKillDone"));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule", "TraceKill finished");
                    }
                    else if (!isExiting)
                    {
                        _announced = false;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"TraceKillUpdate failed: {ex.Message}");
                }
            }
        }

        // Types that have their own specific announcements — skip in generic patches
        private static readonly HashSet<string> _specificTypes = new HashSet<string>
        {
            "Hacknet.PortHackExe",
            "Hacknet.ShellExe",
            "Hacknet.TraceKillExe",
            "Hacknet.NotesExe"
        };

        /// <summary>
        /// Generic Postfix on ExeModule.Completed — announces completion for
        /// any exe module not already handled by a specific patch.
        /// Uses IdentifierName for the announcement text.
        /// </summary>
        [HarmonyPatch]
        static class GenericCompletedPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ExeModule"),
                    "Completed");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string typeName = __instance.GetType().FullName;
                    if (_specificTypes.Contains(typeName)) return;

                    string name = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "IdentifierName")
                        .GetValue(__instance);

                    CleanupProgress(__instance);
                    Plugin.Announce(Loc.Get("exe.genericComplete", name));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Generic complete: {name}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"GenericCompleted failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generic Postfix on ExeModule.LoadContent — announces when any exe
        /// module starts running. Skips types with specific patches.
        /// </summary>
        [HarmonyPatch]
        static class GenericLoadContentPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ExeModule"),
                    "LoadContent");
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string typeName = __instance.GetType().FullName;
                    if (_specificTypes.Contains(typeName)) return;

                    string name = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.ExeModule"), "IdentifierName")
                        .GetValue(__instance);

                    Plugin.Announce(Loc.Get("exe.genericStarted", name), false);
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Generic started: {name}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"GenericLoadContent failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generic Postfix on ExeModule.Update — announces progress at 25/50/75%
        /// for any exe module with recognizable progress fields.
        /// Supports: progress, percentComplete, elapsedTime+RUN_TIME, timeLeft+DURATION.
        /// </summary>
        [HarmonyPatch]
        static class GenericUpdatePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.ExeModule"),
                    "Update", new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    string typeName = __instance.GetType().FullName;
                    if (_specificTypes.Contains(typeName)) return;

                    float progress = GetProgress(__instance);
                    if (progress < 0f) return;

                    var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");
                    int pid = (int)AccessTools.Field(exeModType, "PID").GetValue(__instance);

                    int threshold = (int)(progress * 4); // 0,1,2,3,4
                    if (!_progressThresholds.TryGetValue(pid, out int lastThreshold))
                        lastThreshold = 0;

                    if (threshold > lastThreshold && threshold < 4)
                    {
                        _progressThresholds[pid] = threshold;
                        int percent = threshold * 25;
                        string name = (string)AccessTools.Field(exeModType, "IdentifierName")
                            .GetValue(__instance);
                        Plugin.Announce(Loc.Get("exe.genericProgress", name, percent), false);
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Generic progress: {name} {percent}%");
                    }
                    else if (threshold <= lastThreshold)
                    {
                        // Progress hasn't advanced, keep tracking
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"GenericUpdate failed: {ex.Message}");
                }
            }

            /// <summary>
            /// Attempt to extract a 0-1 progress value from the exe instance
            /// using known field patterns.
            /// </summary>
            private static float GetProgress(object instance)
            {
                var type = instance.GetType();

                // Pattern 1: explicit "progress" field (0→1)
                var field = AccessTools.Field(type, "progress");
                if (field != null && field.FieldType == typeof(float))
                    return (float)field.GetValue(instance);

                // Pattern 2: "percentComplete" field (0→1)
                field = AccessTools.Field(type, "percentComplete");
                if (field != null && field.FieldType == typeof(float))
                    return (float)field.GetValue(instance);

                // Pattern 3: elapsedTime / RUN_TIME
                var elapsedField = AccessTools.Field(type, "elapsedTime");
                var runTimeField = AccessTools.Field(type, "RUN_TIME");
                if (elapsedField != null && runTimeField != null)
                {
                    float elapsed = (float)elapsedField.GetValue(instance);
                    float runTime = (float)runTimeField.GetValue(
                        runTimeField.IsStatic ? null : instance);
                    if (runTime > 0f)
                        return Math.Min(elapsed / runTime, 1f);
                }

                // Pattern 4: timeLeft / DURATION (inverted)
                var timeLeftField = AccessTools.Field(type, "timeLeft");
                var durationField = AccessTools.Field(type, "DURATION");
                if (timeLeftField != null && durationField != null)
                {
                    float timeLeft = (float)timeLeftField.GetValue(instance);
                    float duration = (float)durationField.GetValue(
                        durationField.IsStatic ? null : instance);
                    if (duration > 0f)
                        return Math.Min(1f - timeLeft / duration, 1f);
                }

                return -1f; // No recognizable pattern
            }
        }

        /// <summary>
        /// Clean up progress tracking when an exe completes.
        /// Called from GenericCompletedPatch.
        /// </summary>
        private static void CleanupProgress(object instance)
        {
            try
            {
                int pid = (int)AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.ExeModule"), "PID")
                    .GetValue(instance);
                _progressThresholds.Remove(pid);
            }
            catch { }
        }

        /// <summary>
        /// Patch Button.doButton to handle shell button activation via keyboard.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class ShellButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingShellButtons.Count > 0 && _pendingShellButtons.Remove(myID))
                {
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Activated shell button: {myID}");
                }
            }
        }

        /// <summary>
        /// Process keyboard shortcuts for shell exe modules.
        /// Ctrl+O: overload proxy from all shells.
        /// Ctrl+T: set trap / trigger trap from all shells.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            // Clear stale pending buttons from previous frame
            if (_pendingShellButtons.Count > 0)
            {
                DebugLogger.Log(LogCategory.Handler, "ExeModule",
                    $"Clearing {_pendingShellButtons.Count} stale shell button(s)");
                _pendingShellButtons.Clear();
            }

            // Announce traps that were triggered last frame (batched by IP)
            if (_trapTriggeredIPs.Count > 0)
            {
                string ips = string.Join(", ", _trapTriggeredIPs);
                Plugin.Announce(Loc.Get("exe.shellTrapDone", ips));
                _trapTriggeredIPs.Clear();
            }

            // Announce traps that became ready since last frame
            if (_lastTrapReadyCount > 0)
            {
                Plugin.Announce(Loc.Get("exe.trapsReady", _lastTrapReadyCount));
                DebugLogger.Log(LogCategory.Handler, "ExeModule",
                    $"{_lastTrapReadyCount} trap(s) ready to trigger");
                _lastTrapReadyCount = 0;
            }

            // Monitor opponent location — announce when opponent reaches a trapped machine
            CheckOpponentLocation();

            bool ctrl = currentState.IsKeyDown(Keys.LeftControl)
                     || currentState.IsKeyDown(Keys.RightControl);
            if (!ctrl) return;

            if (!Plugin.IsKeyPressed(Keys.O, currentState)
                && !Plugin.IsKeyPressed(Keys.T, currentState)
                && !Plugin.IsKeyPressed(Keys.W, currentState))
                return;

            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var shellsRaw = AccessTools.Field(osType, "shells")?.GetValue(os) as IList;
                var exes = AccessTools.Field(osType, "exes")?.GetValue(os) as IList;
                if (exes == null) return;

                // Filter to shells still in exes (reboot clears exes but not shells)
                var shells = new List<object>();
                if (shellsRaw != null)
                {
                    foreach (var s in shellsRaw)
                    {
                        if (exes.Contains(s))
                            shells.Add(s);
                    }
                }

                if (shells.Count == 0)
                {
                    Plugin.Announce(Loc.Get("exe.noShells"));
                    return;
                }

                if (Plugin.IsKeyPressed(Keys.O, currentState))
                {
                    // Queue Overload button for all shells
                    foreach (var shell in shells)
                    {
                        int idx = exes.IndexOf(shell);
                        if (idx >= 0)
                            _pendingShellButtons.Add(89200 + idx);
                    }
                    Plugin.Announce(Loc.Get("exe.overloadAll", shells.Count));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Overload queued for {shells.Count} shell(s)");
                }
                else if (Plugin.IsKeyPressed(Keys.T, currentState))
                {
                    // Check shell states to decide trap vs trigger
                    var shellType = AccessTools.TypeByName("Hacknet.ShellExe");
                    var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");
                    var stateField = AccessTools.Field(shellType, "state");
                    var ramCostField = AccessTools.Field(exeModType, "ramCost");
                    var targetRamField = AccessTools.Field(shellType, "targetRamUse");

                    int triggerCount = 0;
                    int trapLoadingCount = 0;
                    int idleCount = 0;
                    foreach (var shell in shells)
                    {
                        int state = (int)stateField.GetValue(shell);
                        int ramCost = (int)ramCostField.GetValue(shell);
                        int targetRam = (int)targetRamField.GetValue(shell);
                        if (state == 2 && ramCost == targetRam)
                        {
                            // Trap ready — queue trigger
                            triggerCount++;
                            int idx = exes.IndexOf(shell);
                            if (idx >= 0)
                                _pendingShellButtons.Add(95000 + idx);
                        }
                        else if (state == 2)
                        {
                            trapLoadingCount++;
                        }
                        else if (state == 0)
                        {
                            idleCount++;
                        }
                    }

                    if (triggerCount > 0)
                    {
                        Plugin.Announce(Loc.Get("exe.triggerAll", triggerCount));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Trigger queued for {triggerCount} shell(s)");
                    }
                    else if (trapLoadingCount > 0)
                    {
                        // Traps are loading but not ready yet
                        Plugin.Announce(Loc.Get("exe.trapLoading", trapLoadingCount));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"{trapLoadingCount} trap(s) still loading");
                    }
                    else if (idleCount > 0)
                    {
                        // Set trap on idle shells
                        foreach (var shell in shells)
                        {
                            int state = (int)stateField.GetValue(shell);
                            if (state == 0)
                            {
                                int idx = exes.IndexOf(shell);
                                if (idx >= 0)
                                    _pendingShellButtons.Add(89300 + idx);
                            }
                        }
                        Plugin.Announce(Loc.Get("exe.trapAll", idleCount));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Trap queued for {idleCount} shell(s)");
                    }
                    else
                    {
                        Plugin.Announce(Loc.Get("exe.noShells"));
                    }
                }
                else if (Plugin.IsKeyPressed(Keys.W, currentState))
                {
                    // Close all shells
                    foreach (var shell in shells)
                    {
                        int idx = exes.IndexOf(shell);
                        if (idx >= 0)
                            _pendingShellButtons.Add(89101 + idx);
                    }
                    Plugin.Announce(Loc.Get("exe.closeAll", shells.Count));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Close queued for {shells.Count} shell(s)");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "ExeModule",
                    $"ProcessInput failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Monitor os.opponentLocation each frame.
        /// When an opponent moves to a machine where the player has a trap set,
        /// announce urgently so the screen reader user knows to trigger (Ctrl+T).
        /// Also announces general opponent location changes.
        /// </summary>
        private static void CheckOpponentLocation()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                string location = (string)AccessTools.Field(osType, "opponentLocation")
                    ?.GetValue(os) ?? "";

                if (location == _lastOpponentLocation) return;
                string previous = _lastOpponentLocation;
                _lastOpponentLocation = location;

                if (string.IsNullOrEmpty(location))
                {
                    if (!string.IsNullOrEmpty(previous))
                    {
                        Plugin.Announce(Loc.Get("exe.opponentGone"), false);
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            "Opponent disconnected");
                    }
                    return;
                }

                // Check if any shell trap is set on this machine
                var shellsRaw = AccessTools.Field(osType, "shells")?.GetValue(os) as IList;
                var exes = AccessTools.Field(osType, "exes")?.GetValue(os) as IList;
                var shellType = AccessTools.TypeByName("Hacknet.ShellExe");
                var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");
                var stateField = AccessTools.Field(shellType, "state");
                var targetField = AccessTools.Field(exeModType, "targetIP");

                bool hasTrapOnTarget = false;
                if (shellsRaw != null && exes != null)
                {
                    foreach (var shell in shellsRaw)
                    {
                        if (!exes.Contains(shell)) continue;
                        int state = (int)stateField.GetValue(shell);
                        string targetIP = (string)targetField.GetValue(shell);
                        if (state == 2 && targetIP == location)
                        {
                            hasTrapOnTarget = true;
                            break;
                        }
                    }
                }

                if (hasTrapOnTarget)
                {
                    Plugin.Announce(Loc.Get("exe.opponentOnTrap", location));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Opponent on trapped machine: {location}");
                }
                else
                {
                    Plugin.Announce(Loc.Get("exe.opponentMoved", location));
                    DebugLogger.Log(LogCategory.Handler, "ExeModule",
                        $"Opponent moved to: {location}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "ExeModule",
                    $"CheckOpponentLocation failed: {ex.Message}");
            }
        }
    }
}
