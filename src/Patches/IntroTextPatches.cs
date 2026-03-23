using System.Reflection;
using HarmonyLib;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches IntroTextModule to announce the intro text lines
    /// (e.g. "14 DAY TIMER EXPIRED : INITIALIZING FAILSAFE").
    /// The game renders these character-by-character via spriteBatch —
    /// no terminal output is used, so we must read the text array directly.
    /// </summary>
    [HarmonyPatch]
    internal static class IntroTextPatches
    {
        private static int _lastAnnouncedIndex = -1;

        [HarmonyPatch]
        static class IntroTextDrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.IntroTextModule"), "Draw",
                    new[] { typeof(float) });
            }

            static void Postfix(object __instance, int ___textIndex, string[] ___text,
                bool ___finishedText, bool ___complete)
            {
                if (___text == null || ___text.Length == 0) return;

                // Announce each new line as it starts being typed
                if (___textIndex > _lastAnnouncedIndex && ___textIndex < ___text.Length)
                {
                    // Announce all lines we may have skipped
                    for (int i = _lastAnnouncedIndex + 1; i <= ___textIndex; i++)
                    {
                        if (i >= 0 && i < ___text.Length
                            && !string.IsNullOrWhiteSpace(___text[i]))
                        {
                            Plugin.Announce(___text[i].Trim(), false);
                        }
                    }
                    _lastAnnouncedIndex = ___textIndex;
                }

                // Announce when intro is complete
                if (___finishedText && _lastAnnouncedIndex < ___text.Length)
                {
                    // Announce any remaining unannounced lines
                    for (int i = _lastAnnouncedIndex + 1; i < ___text.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(___text[i]))
                        {
                            Plugin.Announce(___text[i].Trim(), false);
                        }
                    }
                    _lastAnnouncedIndex = ___text.Length;
                    Plugin.Announce(Loc.Get("intro.complete"));
                }
            }
        }

        /// <summary>
        /// Reset state when a new IntroTextModule is constructed.
        /// </summary>
        [HarmonyPatch]
        static class IntroTextCtorPatch
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Hacknet.IntroTextModule");
                return AccessTools.GetDeclaredConstructors(type)[0];
            }

            static void Postfix()
            {
                _lastAnnouncedIndex = -1;
            }
        }
    }
}
