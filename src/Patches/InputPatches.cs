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
            __result = s;
            return false;
        }
    }

    /// <summary>
    /// Postfix on doTerminalTextField to clear all TextBox flags when any
    /// focus mode is active. Prevents Enter/Up/Down/Tab from leaking.
    /// </summary>
    [HarmonyPatch(typeof(Hacknet.Gui.TextBox), nameof(Hacknet.Gui.TextBox.doTerminalTextField))]
    internal static class SuppressTerminalFlagsPatch
    {
        static void Postfix()
        {
            if (!FocusState.IsAnyFocusActive) return;
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
