using System.Reflection;
using HarmonyLib;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for time-critical notifications: mail, trace, incoming connection.
    /// </summary>
    [HarmonyPatch]
    internal static class NotificationPatches
    {
        // Trace percentage thresholds already announced
        private static bool _trace75;
        private static bool _trace50;
        private static bool _trace25;
        private static bool _trace10;
        private static bool _traceStartAnnounced;

        /// <summary>
        /// After MailIcon.mailReceived, announce new email.
        /// </summary>
        [HarmonyPatch]
        static class MailReceivedPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MailIcon"),
                    "mailReceived",
                    new[] { typeof(string), typeof(string) });
            }

            static void Postfix()
            {
                Plugin.Announce(Loc.Get("notify.mail"));
                DebugLogger.Log(LogCategory.Handler, "Notifications", "New mail received");
            }
        }

        /// <summary>
        /// After IncomingConnectionOverlay.Activate, announce the warning.
        /// </summary>
        [HarmonyPatch]
        static class IncomingConnectionPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Modules.Overlays.IncomingConnectionOverlay"),
                    "Activate");
            }

            static void Postfix()
            {
                Plugin.Announce(Loc.Get("notify.incomingConnection"));
                DebugLogger.Log(LogCategory.Handler, "Notifications", "Incoming connection warning");
            }
        }

        /// <summary>
        /// After TraceTracker.Update, announce trace countdown at thresholds.
        /// </summary>
        [HarmonyPatch]
        static class TraceUpdatePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.TraceTracker"),
                    "Update",
                    new[] { typeof(float) });
            }

            static void Postfix(object __instance)
            {
                var type = __instance.GetType();
                bool active = (bool)(AccessTools.Field(type, "active")?.GetValue(__instance) ?? false);

                if (!active)
                {
                    // Reset thresholds when trace ends
                    if (_traceStartAnnounced)
                    {
                        _traceStartAnnounced = false;
                        _trace75 = false;
                        _trace50 = false;
                        _trace25 = false;
                        _trace10 = false;
                    }
                    return;
                }

                float timer = (float)(AccessTools.Field(type, "timer")?.GetValue(__instance) ?? 0f);
                float startingTimer = (float)(AccessTools.Field(type, "startingTimer")?.GetValue(__instance) ?? 1f);

                if (startingTimer <= 0) return;
                float percent = timer / startingTimer * 100f;

                if (!_traceStartAnnounced)
                {
                    _traceStartAnnounced = true;
                    Plugin.Announce(Loc.Get("notify.traceStarted", (int)startingTimer));
                    DebugLogger.Log(LogCategory.Handler, "Notifications", $"Trace started: {startingTimer}s");
                    return;
                }

                if (!_trace75 && percent <= 75f)
                {
                    _trace75 = true;
                    Plugin.Announce(Loc.Get("notify.tracePercent", 75));
                }
                else if (!_trace50 && percent <= 50f)
                {
                    _trace50 = true;
                    Plugin.Announce(Loc.Get("notify.tracePercent", 50));
                }
                else if (!_trace25 && percent <= 25f)
                {
                    _trace25 = true;
                    Plugin.Announce(Loc.Get("notify.tracePercent", 25));
                }
                else if (!_trace10 && percent <= 10f)
                {
                    _trace10 = true;
                    Plugin.Announce(Loc.Get("notify.traceCritical"));
                }
            }
        }
    }
}
