using System.Collections.Generic;
using System.Reflection;
using Hacknet;
using Hacknet.Gui;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Adds keyboard navigation to the main menu.
    /// Vanilla menu is mouse-only; this adds Up/Down arrow + Enter/Space support.
    /// </summary>
    [HarmonyPatch]
    internal static class MainMenuPatches
    {
        private static readonly List<int> _activeButtons = new List<int>();
        private static int _focusIndex;
        private static int _pendingActivation = -1;
        private static bool _menuAnnounced;
        private static int _lastAnnouncedId = -1;
        private static KeyboardState _prevKeyState;
        private static bool _isDrawingMenuButtons;

        /// <summary>
        /// Prefix on drawMainMenuButtons: handle keyboard input and build button list.
        /// </summary>
        [HarmonyPatch]
        static class DrawButtonsPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("Hacknet.MainMenu"), "drawMainMenuButtons");
            }

            static void Prefix()
            {
                _isDrawingMenuButtons = true;
                // Build active button list each frame (some buttons are conditional)
                _activeButtons.Clear();
                _activeButtons.Add(1);     // New Session
                _activeButtons.Add(1102);  // Continue
                _activeButtons.Add(11);    // Login
                _activeButtons.Add(3);     // Settings
                if (Settings.AllowExtensionMode)
                    _activeButtons.Add(5); // Extensions
                try
                {
                    var dlcType = AccessTools.TypeByName("Hacknet.DLC1SessionUpgrader");
                    if (dlcType != null &&
                        Settings.HasLabyrinthsDemoStartMainMenuButton &&
                        (bool)(AccessTools.Property(dlcType, "HasDLC1Installed")?.GetValue(null) ?? false))
                        _activeButtons.Add(7); // DLC Session
                }
                catch { }
                _activeButtons.Add(15);    // Exit

                // Clamp focus index
                if (_focusIndex >= _activeButtons.Count) _focusIndex = 0;
                if (_focusIndex < 0) _focusIndex = _activeButtons.Count - 1;

                // Handle keyboard navigation with own key state tracking
                // (Plugin._previousKeyState is already consumed by Game1.Update postfix)
                var ks = Keyboard.GetState();

                if (ks.IsKeyDown(Keys.Down) && _prevKeyState.IsKeyUp(Keys.Down))
                {
                    _focusIndex = (_focusIndex + 1) % _activeButtons.Count;
                    DebugLogger.LogInput("Down", "Menu navigate");
                }
                else if (ks.IsKeyDown(Keys.Up) && _prevKeyState.IsKeyUp(Keys.Up))
                {
                    _focusIndex = (_focusIndex - 1 + _activeButtons.Count) % _activeButtons.Count;
                    DebugLogger.LogInput("Up", "Menu navigate");
                }

                if ((ks.IsKeyDown(Keys.Enter) && _prevKeyState.IsKeyUp(Keys.Enter)) ||
                    (ks.IsKeyDown(Keys.Space) && _prevKeyState.IsKeyUp(Keys.Space)))
                {
                    _pendingActivation = _activeButtons[_focusIndex];
                }

                _prevKeyState = ks;
            }
        }

        /// <summary>
        /// Postfix on drawMainMenuButtons: announce menu and current focus.
        /// </summary>
        [HarmonyPatch]
        static class DrawButtonsPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("Hacknet.MainMenu"), "drawMainMenuButtons");
            }

            static void Postfix()
            {
                _isDrawingMenuButtons = false;

                if (!_menuAnnounced)
                {
                    AccessStateManager.TryEnter(AccessStateManager.State.MainMenu);
                    AccessStateManager.SetContext(AccessStateManager.Context.TitleScreen);
                    Plugin.Announce(Loc.Get("menu.main"));
                    _menuAnnounced = true;
                    // Announce first item on menu open
                    AnnounceCurrentFocus();
                }

                // Announce if focus changed (keyboard or mouse)
                int currentId = (_activeButtons.Count > 0 && _focusIndex < _activeButtons.Count)
                    ? _activeButtons[_focusIndex] : -1;

                if (currentId != _lastAnnouncedId && currentId != -1)
                {
                    AnnounceCurrentFocus();
                }
            }
        }

        /// <summary>
        /// Detects when MainMenu leaves Normal state to reset navigation.
        /// </summary>
        [HarmonyPatch]
        static class MainMenuDrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("Hacknet.MainMenu"), "Draw");
            }

            static void Postfix(object __instance)
            {
                var field = AccessTools.Field(__instance.GetType(), "State");
                if (field == null) return;
                int state = (int)field.GetValue(__instance);
                // MainMenuState.Normal = 0
                if (state != 0)
                {
                    Reset();
                }
            }
        }

        /// <summary>
        /// Patch on Button.doButton (7-param version, called by drawMainMenuButtons).
        /// Prefix sets up GuiData state for keyboard activation so the ORIGINAL
        /// click detection code fires naturally (same code path as mouse clicks).
        /// Postfix handles focus highlight and gameplay announcements.
        /// </summary>
        [HarmonyPatch(typeof(Button), nameof(Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Color?))]
        static class ButtonPatch
        {
            /// <summary>
            /// Set up GuiData.hot and GuiData.active BEFORE doButton runs,
            /// so the original click detection logic fires naturally.
            /// The original code checks: hot == myID && active == myID && mouse released → click.
            /// Since the user uses keyboard (mouse is always released), setting both
            /// hot and active makes the original code return true.
            /// </summary>
            static void Prefix(int myID)
            {
                if (!_isDrawingMenuButtons) return;
                if (_pendingActivation == -1 || _pendingActivation != myID) return;

                // Set up state so original click detection fires
                GuiData.hot = myID;
                GuiData.active = myID;
                _pendingActivation = -1;
                DebugLogger.LogInput("Enter", $"Activate button {myID}");
            }

            static void Postfix(int myID, string text, ref bool __result)
            {
                // Only handle focus during drawMainMenuButtons
                if (_isDrawingMenuButtons)
                {
                    if (__result)
                    {
                        DebugLogger.Log(LogCategory.Handler, "MainMenu", $"Activated: {text}");
                    }

                    // Force GuiData.hot for keyboard-focused menu button
                    if (_activeButtons.Count > 0 && _focusIndex < _activeButtons.Count
                        && myID == _activeButtons[_focusIndex])
                    {
                        GuiData.hot = myID;
                    }
                    return;
                }

                // Gameplay button focus announcements
                if (AccessStateManager.IsIn(AccessStateManager.State.Gameplay))
                {
                    HandleGameplay(myID, text);
                }
            }

            private static int _lastHotButton = -1;

            private static void HandleGameplay(int myID, string text)
            {
                // Skip main menu button IDs — they can leak into gameplay
                // when GuiData.hot retains stale values after menu-to-game transition
                if (myID == 1 || myID == 1102 || myID == 11 || myID == 3 ||
                    myID == 5 || myID == 7 || myID == 15)
                    return;

                if (GuiData.hot == myID && myID != _lastHotButton)
                {
                    _lastHotButton = myID;
                    Plugin.Announce(text);
                    DebugLogger.Log(LogCategory.Handler, "ButtonFocus",
                        $"Button focus: {myID} = {text}");
                }
                else if (GuiData.hot != myID && _lastHotButton == myID)
                {
                    _lastHotButton = -1;
                }
            }
        }

        private static void AnnounceCurrentFocus()
        {
            if (_activeButtons.Count == 0 || _focusIndex >= _activeButtons.Count) return;

            int buttonId = _activeButtons[_focusIndex];
            string name = GetButtonName(buttonId);
            if (name == null) return;

            _lastAnnouncedId = buttonId;
            int position = _focusIndex + 1;
            Plugin.Announce(Loc.Get("menu.item", name, position, _activeButtons.Count));
        }

        private static string GetButtonName(int buttonId)
        {
            return buttonId switch
            {
                1 => Loc.Get("menu.newSession"),
                1102 => Loc.Get("menu.continue"),
                11 => Loc.Get("menu.login"),
                3 => Loc.Get("menu.settings"),
                5 => Loc.Get("menu.extensions"),
                7 => Loc.Get("menu.dlcSession"),
                15 => Loc.Get("menu.exit"),
                _ => null
            };
        }

        /// <summary>
        /// Reset navigation state.
        /// </summary>
        public static void Reset()
        {
            _focusIndex = 0;
            _menuAnnounced = false;
            _lastAnnouncedId = -1;
            _pendingActivation = -1;
            _activeButtons.Clear();
            _isDrawingMenuButtons = false;
        }
    }
}
