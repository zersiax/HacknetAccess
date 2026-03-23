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
        /// Postfix on ShellExe.completedAction — announce overload/trap completed.
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

            static void Postfix(int action, object __instance)
            {
                try
                {
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
                        Plugin.Announce(Loc.Get("exe.shellTrapDone", destIP));
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

            bool ctrl = currentState.IsKeyDown(Keys.LeftControl)
                     || currentState.IsKeyDown(Keys.RightControl);
            if (!ctrl) return;

            if (!Plugin.IsKeyPressed(Keys.O, currentState)
                && !Plugin.IsKeyPressed(Keys.T, currentState))
                return;

            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var shells = AccessTools.Field(osType, "shells")?.GetValue(os) as IList;
                if (shells == null || shells.Count == 0)
                {
                    Plugin.Announce(Loc.Get("exe.noShells"));
                    return;
                }

                var exes = AccessTools.Field(osType, "exes")?.GetValue(os) as IList;
                if (exes == null) return;

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
                    // Check if any shell has a ready trigger (state==2, RAM allocated)
                    var shellType = AccessTools.TypeByName("Hacknet.ShellExe");
                    var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");
                    var stateField = AccessTools.Field(shellType, "state");
                    var ramCostField = AccessTools.Field(exeModType, "ramCost");
                    var targetRamField = AccessTools.Field(shellType, "targetRamUse");

                    bool anyTriggerReady = false;
                    foreach (var shell in shells)
                    {
                        int state = (int)stateField.GetValue(shell);
                        int ramCost = (int)ramCostField.GetValue(shell);
                        int targetRam = (int)targetRamField.GetValue(shell);
                        if (state == 2 && ramCost == targetRam)
                        {
                            anyTriggerReady = true;
                            int idx = exes.IndexOf(shell);
                            if (idx >= 0)
                                _pendingShellButtons.Add(95000 + idx);
                        }
                    }

                    if (anyTriggerReady)
                    {
                        Plugin.Announce(Loc.Get("exe.triggerAll"));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            "Trigger queued for ready shell(s)");
                    }
                    else
                    {
                        // Set trap on all shells
                        foreach (var shell in shells)
                        {
                            int idx = exes.IndexOf(shell);
                            if (idx >= 0)
                                _pendingShellButtons.Add(89300 + idx);
                        }
                        Plugin.Announce(Loc.Get("exe.trapAll", shells.Count));
                        DebugLogger.Log(LogCategory.Handler, "ExeModule",
                            $"Trap queued for {shells.Count} shell(s)");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "ExeModule",
                    $"ProcessInput failed: {ex.Message}");
            }
        }
    }
}
