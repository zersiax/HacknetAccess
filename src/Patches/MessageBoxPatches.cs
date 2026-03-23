using HarmonyLib;
using Microsoft.Xna.Framework;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for MessageBoxScreen modal dialogs.
    /// Announces dialog text and button labels on activation.
    /// MessageBoxScreen is internal, so we use AccessTools.TypeByName.
    /// </summary>
    [HarmonyPatch]
    internal static class MessageBoxPatches
    {
        private static string _lastMessage;

        /// <summary>
        /// After MessageBoxScreen.Draw, announce dialog content on first appearance.
        /// </summary>
        [HarmonyPatch]
        static class DrawPatch
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Hacknet.MessageBoxScreen");
                return AccessTools.Method(type, "Draw", new[] { typeof(GameTime) });
            }

            static void Postfix(object __instance)
            {
                string message = ReflectionHelper.GetPrivateField<string>(__instance, "message");
                if (message == null || message == _lastMessage) return;

                _lastMessage = message;
                AccessStateManager.TryEnter(AccessStateManager.State.MessageBox);

                string acceptText = (string)AccessTools.Field(__instance.GetType(), "OverrideAcceptedText")
                    ?.GetValue(__instance) ?? "Quit Hacknet";
                string cancelText = (string)AccessTools.Field(__instance.GetType(), "OverrideCancelText")
                    ?.GetValue(__instance) ?? "Resume";

                string announcement = Loc.Get("dialog.message", message, acceptText, cancelText);
                Plugin.Announce(announcement);
                DebugLogger.Log(LogCategory.Handler, "MessageBox", $"Dialog: {message}");
            }
        }

        /// <summary>
        /// Reset tracking when a MessageBoxScreen exits.
        /// </summary>
        public static void Reset()
        {
            _lastMessage = null;
        }
    }
}
