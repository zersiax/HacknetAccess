using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;


namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches the game's update loop to process mod input keys every frame.
    /// </summary>
    [HarmonyPatch(typeof(Hacknet.Game1), "Update", typeof(GameTime))]
    internal static class Game1UpdatePatch
    {
        static void Postfix()
        {
            AccessStateManager.Tick();
            Plugin.ProcessInput();
        }
    }

    /// <summary>
    /// Checks whether any accessibility focus mode is currently active.
    /// Used by input suppression patches.
    /// </summary>
    internal static class FocusState
    {
        public static bool IsAnyFocusActive =>
            DisplayModulePatches.DisplayHasFocus
            || MailPatches.HasFocus
            || NotesPatches.HasFocus
            || NetworkMapPatches.HasFocus;
    }

    /// <summary>
    /// Patches TextBox.getFilteredStringInput (private) to return the original
    /// string unchanged when any focus mode is active. This prevents typed
    /// characters from reaching the terminal input buffer.
    /// Exception: when the terminal has an active getString/login prompt,
    /// allow typing through so the user can fill in the prompt.
    /// </summary>
    [HarmonyPatch]
    internal static class SuppressCharacterInputPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Hacknet.Gui.TextBox), "getFilteredStringInput");
        }

        static bool Prefix(string s, ref string __result)
        {
            if (!FocusState.IsAnyFocusActive) return true;
            // Allow typing into user-driven prompts (search, email address, etc.).
            // Skip the bypass during daemon-driven login flows where forceLogin
            // injects credentials directly — letting Enter/keys through there
            // interferes with the automated username/password sequence.
            if (TerminalPatches.IsTerminalInputMode()
                && !TerminalPatches.SuppressPromptAnnounce) return true;
            __result = s;
            return false;
        }
    }

    /// <summary>
    /// Postfix on doTerminalTextField to clear all TextBox flags when any
    /// focus mode is active. Prevents Enter/Up/Down/Tab from leaking.
    /// Exception: when a user-driven getString prompt is active, leave Enter
    /// intact so the user can submit. Daemon-driven logins are still suppressed.
    /// </summary>
    [HarmonyPatch(typeof(Hacknet.Gui.TextBox), nameof(Hacknet.Gui.TextBox.doTerminalTextField))]
    internal static class SuppressTerminalFlagsPatch
    {
        static void Postfix()
        {
            if (!FocusState.IsAnyFocusActive) return;
            if (TerminalPatches.IsTerminalInputMode()
                && !TerminalPatches.SuppressPromptAnnounce) return;
            Hacknet.Gui.TextBox.BoxWasActivated = false;
            Hacknet.Gui.TextBox.UpWasPresed = false;
            Hacknet.Gui.TextBox.DownWasPresed = false;
            Hacknet.Gui.TextBox.TabWasPresed = false;
        }
    }

    /// <summary>
    /// Detects when the OS (main gameplay) loads and sets accessibility context.
    /// </summary>
    [HarmonyPatch]
    internal static class OSLoadPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("Hacknet.OS"), "LoadContent");
        }

        static void Postfix()
        {
            AccessStateManager.SetContext(AccessStateManager.Context.Gameplay);
            AccessStateManager.TryEnter(AccessStateManager.State.Gameplay);
            Plugin.Announce(Loc.Get("game.started"), false);
            DebugLogger.Log(LogCategory.State, "OS", "Gameplay started");
        }
    }
}
