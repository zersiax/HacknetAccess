using System;
using System.Reflection;
using HarmonyLib;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for TraceDangerSequence — the critical danger sequence
    /// that triggers when a trace completes. Player MUST know about this.
    /// </summary>
    [HarmonyPatch]
    internal static class TraceDangerPatches
    {
        private static int _lastDangerState = -1;
        private static int _lastCountdownAnnounced = -1;

        /// <summary>
        /// Postfix on TraceDangerSequence.BeginTraceDangerSequence —
        /// announce critical danger.
        /// </summary>
        [HarmonyPatch]
        static class BeginDangerPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Effects.TraceDangerSequence"),
                    "BeginTraceDangerSequence");
            }

            static void Postfix()
            {
                _lastDangerState = -1;
                _lastCountdownAnnounced = -1;
                Plugin.Announce(Loc.Get("trace.danger"));
                DebugLogger.Log(LogCategory.State, "TraceDanger sequence started");
            }
        }

        /// <summary>
        /// Postfix on TraceDangerSequence.CompleteIPResetSucsesfully —
        /// announce successful recovery.
        /// </summary>
        [HarmonyPatch]
        static class IPResetPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Effects.TraceDangerSequence"),
                    "CompleteIPResetSucsesfully");
            }

            static void Postfix()
            {
                Plugin.Announce(Loc.Get("trace.ipReset"));
                DebugLogger.Log(LogCategory.State, "IP reset successful");
                _lastDangerState = -1;
                _lastCountdownAnnounced = -1;
            }
        }

        /// <summary>
        /// Postfix on TraceDangerSequence.Update — announce state transitions
        /// and countdown intervals.
        /// </summary>
        [HarmonyPatch]
        static class DangerUpdatePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Effects.TraceDangerSequence"),
                    "Update", new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    bool isActive = (bool)AccessTools.Field(type, "IsActive").GetValue(__instance);
                    if (!isActive) return;

                    // TraceDangerState is private enum, read as int
                    int stateVal = (int)AccessTools.Field(type, "state").GetValue(__instance);
                    float timeThisState = (float)AccessTools.Field(type, "timeThisState").GetValue(__instance);

                    if (stateVal != _lastDangerState)
                    {
                        _lastDangerState = stateVal;
                        _lastCountdownAnnounced = -1;

                        switch (stateVal)
                        {
                            case 1: // WarningScreen
                                Plugin.Announce(Loc.Get("trace.pressBegin"));
                                break;
                            case 3: // Countdown
                                Plugin.Announce(Loc.Get("trace.countdown", 130));
                                _lastCountdownAnnounced = 130;
                                break;
                            case 4: // Gameover
                                Plugin.Announce(Loc.Get("trace.gameover"));
                                break;
                            case 5: // DisconnectedReboot
                                // Already handled by IPResetPatch
                                break;
                        }
                    }

                    // During countdown (state 3), announce time remaining at intervals
                    if (stateVal == 3)
                    {
                        int secondsRemaining = (int)(130f - timeThisState);
                        if (secondsRemaining < 0) secondsRemaining = 0;

                        // Announce at 120, 100, 90, 60, 30, 20, 10, 5
                        int announceAt = -1;
                        if (secondsRemaining <= 5 && _lastCountdownAnnounced > 5)
                            announceAt = 5;
                        else if (secondsRemaining <= 10 && _lastCountdownAnnounced > 10)
                            announceAt = 10;
                        else if (secondsRemaining <= 20 && _lastCountdownAnnounced > 20)
                            announceAt = 20;
                        else if (secondsRemaining <= 30 && _lastCountdownAnnounced > 30)
                            announceAt = 30;
                        else if (secondsRemaining <= 60 && _lastCountdownAnnounced > 60)
                            announceAt = 60;
                        else if (secondsRemaining <= 90 && _lastCountdownAnnounced > 90)
                            announceAt = 90;
                        else if (secondsRemaining <= 100 && _lastCountdownAnnounced > 100)
                            announceAt = 100;
                        else if (secondsRemaining <= 120 && _lastCountdownAnnounced > 120)
                            announceAt = 120;

                        if (announceAt > 0)
                        {
                            _lastCountdownAnnounced = announceAt;
                            Plugin.Announce(Loc.Get("trace.countdown", announceAt));
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "TraceDanger",
                        $"Update failed: {ex.Message}");
                }
            }
        }
    }
}
