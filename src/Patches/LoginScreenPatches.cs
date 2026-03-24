using Hacknet.UIUtils;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for the login/account creation screen.
    /// Announces new history lines and prompt changes.
    /// </summary>
    internal static class LoginScreenPatches
    {
        private static string _lastPrompt;

        /// <summary>
        /// After WriteToHistory, announce the new line.
        /// </summary>
        [HarmonyPatch(typeof(SavefileLoginScreen), nameof(SavefileLoginScreen.WriteToHistory))]
        static class WriteToHistoryPatch
        {
            static void Postfix(string message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Plugin.Announce(message);
                    // Reset prompt tracking so the next prompt is re-announced
                    // even if it's the same (e.g. "USERNAME :" after validation error)
                    _lastPrompt = null;
                    DebugLogger.Log(LogCategory.Handler, "LoginScreen", $"History: {message}");
                }
            }
        }

        /// <summary>
        /// After Draw, detect prompt changes and password mode.
        /// </summary>
        [HarmonyPatch(typeof(SavefileLoginScreen), nameof(SavefileLoginScreen.Draw),
            typeof(SpriteBatch), typeof(Rectangle))]
        static class DrawPatch
        {
            static void Postfix(SavefileLoginScreen __instance,
                string ___currentPrompt, bool ___InPasswordMode)
            {
                AccessStateManager.TryEnter(AccessStateManager.State.LoginScreen);

                if (___currentPrompt != null && ___currentPrompt != _lastPrompt)
                {
                    _lastPrompt = ___currentPrompt;
                    string announcement = ___InPasswordMode
                        ? Loc.Get("login.promptPassword", ___currentPrompt)
                        : Loc.Get("login.prompt", ___currentPrompt);
                    Plugin.Announce(announcement);
                    DebugLogger.Log(LogCategory.Handler, "LoginScreen", $"Prompt: {___currentPrompt}");
                }
            }
        }

        /// <summary>
        /// After ResetForNewAccount, announce the mode.
        /// </summary>
        [HarmonyPatch(typeof(SavefileLoginScreen), nameof(SavefileLoginScreen.ResetForNewAccount))]
        static class ResetNewAccountPatch
        {
            static void Postfix()
            {
                _lastPrompt = null;
                Plugin.Announce(Loc.Get("login.newAccount"));
            }
        }

        /// <summary>
        /// After ResetForLogin, announce the mode.
        /// </summary>
        [HarmonyPatch(typeof(SavefileLoginScreen), nameof(SavefileLoginScreen.ResetForLogin))]
        static class ResetLoginPatch
        {
            static void Postfix()
            {
                _lastPrompt = null;
                Plugin.Announce(Loc.Get("login.existingAccount"));
            }
        }
    }
}
