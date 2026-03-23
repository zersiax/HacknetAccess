using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for RAM module. F4 reads RAM status.
    /// Announces out-of-memory warnings.
    /// </summary>
    [HarmonyPatch]
    internal static class RamModulePatches
    {
        private static bool _oomWarned;

        /// <summary>
        /// After RamModule.Draw, check for OOM warning state.
        /// </summary>
        [HarmonyPatch]
        static class FlashWarningPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.RamModule"),
                    "FlashMemoryWarning");
            }

            static void Postfix()
            {
                if (!_oomWarned)
                {
                    _oomWarned = true;
                    Plugin.Announce(Loc.Get("ram.warning"));
                    DebugLogger.Log(LogCategory.Handler, "RamModule", "OOM warning");
                }
            }
        }

        /// <summary>
        /// Process F4 shortcut to read RAM status.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!Plugin.IsKeyPressed(Keys.F4, currentState)) return;

            DebugLogger.LogInput("F4", "Read RAM status");

            var osType = AccessTools.TypeByName("Hacknet.OS");
            var currentInstance = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
            if (currentInstance == null)
            {
                Plugin.Announce(Loc.Get("ram.unavailable"));
                return;
            }

            int totalRam = (int)(AccessTools.Field(osType, "totalRam")?.GetValue(currentInstance) ?? 0);
            int ramAvailable = (int)(AccessTools.Field(osType, "ramAvaliable")?.GetValue(currentInstance) ?? 0);
            int used = totalRam - ramAvailable;

            // Reset OOM flag when RAM frees up
            if (ramAvailable > 0) _oomWarned = false;

            Plugin.Announce(Loc.Get("ram.status", used, totalRam));
        }
    }
}
