using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Makes the AdvancedTutorial accessible.
    /// Announces tutorial text on state changes and makes Continue/Hint buttons
    /// keyboard-accessible (Enter to activate Continue, H for Hint).
    /// </summary>
    [HarmonyPatch]
    internal static class TutorialPatches
    {
        private static int _lastState = -1;
        private static bool _continueAnnounced;
        private static KeyboardState _prevKeyState;

        /// <summary>
        /// Postfix on AdvancedTutorial.Draw — announce state changes,
        /// handle keyboard for Continue/Hint buttons.
        /// </summary>
        [HarmonyPatch]
        static class AdvancedTutorialDrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AdvancedTutorial"), "Draw",
                    new[] { typeof(float) });
            }

            static void Postfix(object __instance, int ___state, string[] ___renderText)
            {
                // Announce tutorial text when state changes
                if (___state != _lastState)
                {
                    _lastState = ___state;
                    _continueAnnounced = false;

                    if (___renderText != null && ___renderText.Length > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (string line in ___renderText)
                        {
                            if (!string.IsNullOrWhiteSpace(line)
                                && !line.Contains("<#") && !line.Contains("#>"))
                            {
                                string clean = line.Trim();
                                // Strip #markers# used for highlighting
                                clean = Regex.Replace(clean, "#([^#]*)#", "$1");
                                if (clean.Length > 0)
                                {
                                    sb.Append(clean);
                                    sb.Append(" ");
                                }
                            }
                        }
                        string text = sb.ToString().Trim();
                        if (text.Length > 0)
                        {
                            // Replace inaccessible instructions with accessible ones
                            text = ReplaceInaccessibleInstructions(text);

                            // Append accessibility hints for visual-only steps
                            string hint = GetAccessibilityHint(___state);
                            if (hint != null)
                                text += " " + hint;

                            // Use interrupt=false so tutorial text isn't cut off
                            Plugin.Announce(Loc.Get("tutorial.step", text), false);
                        }
                    }
                }

                // Announce Continue button when at state 0
                if (___state == 0 && !_continueAnnounced)
                {
                    _continueAnnounced = true;
                    Plugin.Announce(Loc.Get("tutorial.pressContinue"), false);
                }

                // Force terminal visible during tutorial so blind users can type commands
                // The game hides it for early states (expects mouse clicks on network map)
                ForceTerminalVisible();
            }
        }

        /// <summary>
        /// Prefix on AdvancedTutorial.Draw — inject Enter key to activate Continue button
        /// and H key for Hint button.
        /// </summary>
        [HarmonyPatch]
        static class AdvancedTutorialDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AdvancedTutorial"), "Draw",
                    new[] { typeof(float) });
            }

            private static int _pendingButton = -1;

            static void Prefix(int ___state)
            {
                var ks = Keyboard.GetState();

                if (___state == 0)
                {
                    // Enter activates Continue
                    if (ks.IsKeyDown(Keys.Enter) && _prevKeyState.IsKeyUp(Keys.Enter))
                    {
                        _pendingButton = 2933201;
                    }
                }

                // H activates Hint (available in later states)
                if (ks.IsKeyDown(Keys.H) && _prevKeyState.IsKeyUp(Keys.H))
                {
                    _pendingButton = 2933202;
                }

                _prevKeyState = ks;
            }

            /// <summary>
            /// Consume pending button activation via the existing ButtonPatch.
            /// We need a separate patch on Button.doButton for this.
            /// </summary>
            public static int ConsumePending()
            {
                int val = _pendingButton;
                _pendingButton = -1;
                return val;
            }
        }

        /// <summary>
        /// Patch Button.doButton to handle tutorial button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class TutorialButtonPatch
        {
            static void Prefix(int myID, string text)
            {
                int pending = AdvancedTutorialDrawPrefix.ConsumePending();
                if (pending != -1 && pending == myID)
                {
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "Tutorial",
                        $"Activated button: {text}");
                }
            }
        }

        /// <summary>
        /// Forces the terminal module visible so blind users can type commands.
        /// The vanilla game hides it during early tutorial states (expecting mouse clicks).
        /// </summary>
        private static void ForceTerminalVisible()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var terminal = AccessTools.Field(osType, "terminal")?.GetValue(os);
                if (terminal == null) return;

                var visibleField = AccessTools.Field(terminal.GetType(), "visible");
                var inputLockedField = AccessTools.Field(terminal.GetType(), "inputLocked");
                if (visibleField != null && !(bool)visibleField.GetValue(terminal))
                {
                    visibleField.SetValue(terminal, true);
                }
                if (inputLockedField != null && (bool)inputLockedField.GetValue(terminal))
                {
                    inputLockedField.SetValue(terminal, false);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Tutorial", $"ForceTerminalVisible: {ex.Message}");
            }
        }

        /// <summary>
        /// Replace inaccessible instructions in the tutorial text with
        /// accessible equivalents so users aren't confused by references
        /// to buttons they can't interact with.
        /// </summary>
        private static string ReplaceInaccessibleInstructions(string text)
        {
            // "pressing the Scan Network button on the display module" → "typing scan"
            text = text.Replace(
                "by pressing the Scan Network button on the display module.",
                "by typing scan in the terminal.");
            // "clicking the green circle" → use F3 or connect command
            text = text.Replace(
                "by clicking the green circle.",
                "using F3 to open the network map, or by typing connect followed by your IP.");
            // "clicking a blue node on the network map" → use F3
            text = text.Replace(
                "by clicking a blue node on the network map.",
                "using F3 to open the network map and selecting a node, or by typing connect followed by the IP.");
            return text;
        }

        /// <summary>
        /// Returns an accessibility hint to append to the tutorial text for
        /// steps that reference visual-only mechanics (clicking nodes, etc.).
        /// </summary>
        private static string GetAccessibilityHint(int state)
        {
            switch (state)
            {
                case 1:
                    // "click the green circle" → use F3 or type connect
                    return Loc.Get("tutorial.hint.connectSelf");
                case 2:
                    // "pressing the Scan Network button" → type scan
                    return Loc.Get("tutorial.hint.scan");
                case 4:
                    // "clicking a blue node on the network map" → use F3
                    return Loc.Get("tutorial.hint.connectOther");
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reset state when tutorial is constructed.
        /// </summary>
        [HarmonyPatch]
        static class AdvancedTutorialCtorPatch
        {
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("Hacknet.AdvancedTutorial");
                return AccessTools.GetDeclaredConstructors(type)[0];
            }

            static void Postfix()
            {
                _lastState = -1;
                _continueAnnounced = false;
            }
        }
    }
}
