using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for the Options/Settings menu.
    /// Up/Down navigates controls, Enter/Space toggles checkboxes,
    /// Left/Right adjusts slider, Enter opens lists, Escape closes menu.
    /// </summary>
    [HarmonyPatch]
    internal static class OptionsMenuPatches
    {
        private static bool _menuAnnounced;
        private static KeyboardState _prevKeyState;
        private static int _frameCount;
        private static object _menuInstance;

        // Virtual focus list
        private static List<SettingsControl> _controls = new List<SettingsControl>();
        private static int _focusIndex;
        private static SettingsState _state = SettingsState.ControlList;
        private static int _listSubIndex;

        // Pending button for Back / Apply
        private static int _pendingButton = -1;

        enum SettingsState { ControlList, ListSelect, SliderAdjust }
        enum ControlType { Checkbox, Slider, List, Button }

        class SettingsControl
        {
            public string Label;
            public ControlType Type;
            public int GuiId;
        }

        /// <summary>
        /// Whether the settings menu is currently open.
        /// </summary>
        public static bool IsActive => _menuAnnounced;

        /// <summary>
        /// After OptionsMenu.Draw, detect open/close and track instance.
        /// </summary>
        [HarmonyPatch]
        static class DrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.OptionsMenu"),
                    "Draw",
                    new[] { typeof(GameTime) });
            }

            static void Postfix(object __instance)
            {
                _menuInstance = __instance;

                if (!_menuAnnounced)
                {
                    _menuAnnounced = true;
                    _frameCount = 0;
                    _prevKeyState = Keyboard.GetState();
                    _state = SettingsState.ControlList;
                    _focusIndex = 0;
                    BuildControlList(__instance);
                    AccessStateManager.TryEnter(AccessStateManager.State.OptionsMenu);
                    Plugin.Announce(Loc.Get("settings.opened", _controls.Count));
                    DebugLogger.Log(LogCategory.Handler, "OptionsMenu", "Settings opened");
                    return;
                }

                _frameCount++;
            }
        }

        /// <summary>
        /// Detect when OptionsMenu is exiting via other means.
        /// </summary>
        [HarmonyPatch]
        static class HandleInputPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.OptionsMenu"),
                    "HandleInput");
            }

            static void Postfix(object __instance)
            {
                var screenState = AccessTools.Property(__instance.GetType(), "ScreenState");
                if (screenState != null)
                {
                    int state = (int)screenState.GetValue(__instance, null);
                    if (state == 2 || state == 3)
                    {
                        if (_menuAnnounced)
                        {
                            _menuAnnounced = false;
                            AccessStateManager.Exit(AccessStateManager.State.OptionsMenu);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Patch Button.doButton to handle settings button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class SettingsButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                        $"Activated button: {myID}");
                }
            }
        }

        /// <summary>
        /// Build the virtual control list from the menu instance.
        /// </summary>
        private static void BuildControlList(object menu)
        {
            _controls.Clear();

            try
            {
                var type = menu.GetType();
                bool fromGame = (bool)AccessTools.Field(type, "startedFromGameContext")
                    .GetValue(menu);

                _controls.Add(new SettingsControl
                    { Label = "Resolution", Type = ControlType.List, GuiId = 10 });

                if (!fromGame)
                {
                    _controls.Add(new SettingsControl
                        { Label = "Language", Type = ControlType.List, GuiId = 1013 });
                }

                _controls.Add(new SettingsControl
                    { Label = "Fullscreen", Type = ControlType.Checkbox, GuiId = 20 });
                _controls.Add(new SettingsControl
                    { Label = "Bloom", Type = ControlType.Checkbox, GuiId = 21 });
                _controls.Add(new SettingsControl
                    { Label = "Scanlines", Type = ControlType.Checkbox, GuiId = 22 });
                _controls.Add(new SettingsControl
                    { Label = "Multisampling", Type = ControlType.Checkbox, GuiId = 221 });
                _controls.Add(new SettingsControl
                    { Label = "Audio Visualiser", Type = ControlType.Checkbox, GuiId = 223 });
                _controls.Add(new SettingsControl
                    { Label = "Sound Enabled", Type = ControlType.Checkbox, GuiId = 23 });
                _controls.Add(new SettingsControl
                    { Label = "Music Volume", Type = ControlType.Slider, GuiId = 24 });
                _controls.Add(new SettingsControl
                    { Label = "Text Size", Type = ControlType.List, GuiId = 25 });
                _controls.Add(new SettingsControl
                    { Label = "Apply Changes", Type = ControlType.Button, GuiId = 9907 });
                _controls.Add(new SettingsControl
                    { Label = "Back", Type = ControlType.Button, GuiId = 999 });
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"BuildControlList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Read the current value of a control for announcement.
        /// </summary>
        private static string ReadControlValue(SettingsControl ctrl)
        {
            try
            {
                if (_menuInstance == null) return "";
                var type = _menuInstance.GetType();

                switch (ctrl.GuiId)
                {
                    case 10: // Resolution
                    {
                        var resolutions = AccessTools.Field(type, "resolutions")
                            .GetValue(_menuInstance) as string[];
                        int idx = (int)AccessTools.Field(type, "currentResIndex")
                            .GetValue(_menuInstance);
                        if (resolutions != null && idx >= 0 && idx < resolutions.Length)
                            return resolutions[idx];
                        return "Unknown";
                    }
                    case 1013: // Language
                    {
                        var locales = AccessTools.Field(type, "localeNames")
                            .GetValue(_menuInstance) as string[];
                        int idx = (int)AccessTools.Field(type, "currentLocaleIndex")
                            .GetValue(_menuInstance);
                        if (locales != null && idx >= 0 && idx < locales.Length)
                            return locales[idx];
                        return "Unknown";
                    }
                    case 20: // Fullscreen (windowed is inverted)
                    {
                        bool windowed = (bool)AccessTools.Field(type, "windowed")
                            .GetValue(_menuInstance);
                        return windowed ? "On" : "Off";
                    }
                    case 21: // Bloom
                    {
                        var ppType = AccessTools.TypeByName("Hacknet.PostProcessor");
                        bool val = (bool)AccessTools.Field(ppType, "bloomEnabled").GetValue(null);
                        return val ? "On" : "Off";
                    }
                    case 22: // Scanlines
                    {
                        var ppType = AccessTools.TypeByName("Hacknet.PostProcessor");
                        bool val = (bool)AccessTools.Field(ppType, "scanlinesEnabled").GetValue(null);
                        return val ? "On" : "Off";
                    }
                    case 221: // Multisampling
                    {
                        var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                        bool val = (bool)AccessTools.Field(slType, "ShouldMultisample").GetValue(null);
                        return val ? "On" : "Off";
                    }
                    case 223: // Audio Visualiser
                    {
                        var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                        bool val = (bool)AccessTools.Field(slType, "ShouldDrawMusicVis").GetValue(null);
                        return val ? "On" : "Off";
                    }
                    case 23: // Sound Enabled (inverted from isMuted)
                    {
                        var mmType = AccessTools.TypeByName("Hacknet.MusicManager");
                        bool muted = (bool)AccessTools.Field(mmType, "isMuted").GetValue(null);
                        return muted ? "Off" : "On";
                    }
                    case 24: // Music Volume
                    {
                        var mmType = AccessTools.TypeByName("Hacknet.MusicManager");
                        float vol = (float)AccessTools.Method(mmType, "getVolume")
                            .Invoke(null, null);
                        return $"{(int)(vol * 100)} percent";
                    }
                    case 25: // Text Size
                    {
                        var fonts = AccessTools.Field(type, "fontConfigs")
                            .GetValue(_menuInstance) as string[];
                        int idx = (int)AccessTools.Field(type, "currentFontIndex")
                            .GetValue(_menuInstance);
                        if (fonts != null && idx >= 0 && idx < fonts.Length)
                            return fonts[idx];
                        return "Default";
                    }
                    case 9907:
                    case 999:
                        return "";
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"ReadControlValue failed: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// Announce the currently focused control with its type and value.
        /// </summary>
        private static void AnnounceFocused()
        {
            if (_focusIndex < 0 || _focusIndex >= _controls.Count) return;
            var ctrl = _controls[_focusIndex];
            string value = ReadControlValue(ctrl);

            string typeStr;
            switch (ctrl.Type)
            {
                case ControlType.Checkbox:
                    typeStr = $"checkbox, {value}";
                    break;
                case ControlType.Slider:
                    typeStr = value;
                    break;
                case ControlType.List:
                    typeStr = $"current: {value}";
                    break;
                case ControlType.Button:
                    typeStr = "button";
                    break;
                default:
                    typeStr = "";
                    break;
            }

            Plugin.Announce(Loc.Get("settings.control",
                ctrl.Label, typeStr, _focusIndex + 1, _controls.Count));
        }

        /// <summary>
        /// Toggle a checkbox and announce the new value.
        /// </summary>
        private static void ToggleCheckbox(SettingsControl ctrl)
        {
            try
            {
                if (_menuInstance == null) return;
                var type = _menuInstance.GetType();

                switch (ctrl.GuiId)
                {
                    case 20: // Fullscreen (windowed)
                    {
                        bool windowed = (bool)AccessTools.Field(type, "windowed")
                            .GetValue(_menuInstance);
                        windowed = !windowed;
                        AccessTools.Field(type, "windowed").SetValue(_menuInstance, windowed);
                        AccessTools.Field(type, "resolutionChanged")
                            .SetValue(_menuInstance, true);
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            windowed ? "On" : "Off"));
                        break;
                    }
                    case 21: // Bloom
                    {
                        var ppType = AccessTools.TypeByName("Hacknet.PostProcessor");
                        var field = AccessTools.Field(ppType, "bloomEnabled");
                        bool val = !(bool)field.GetValue(null);
                        field.SetValue(null, val);
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            val ? "On" : "Off"));
                        break;
                    }
                    case 22: // Scanlines
                    {
                        var ppType = AccessTools.TypeByName("Hacknet.PostProcessor");
                        var field = AccessTools.Field(ppType, "scanlinesEnabled");
                        bool val = !(bool)field.GetValue(null);
                        field.SetValue(null, val);
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            val ? "On" : "Off"));
                        break;
                    }
                    case 221: // Multisampling
                    {
                        var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                        var field = AccessTools.Field(slType, "ShouldMultisample");
                        bool val = !(bool)field.GetValue(null);
                        field.SetValue(null, val);
                        AccessTools.Field(type, "resolutionChanged")
                            .SetValue(_menuInstance, true);
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            val ? "On" : "Off"));
                        break;
                    }
                    case 223: // Audio Visualiser
                    {
                        var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                        var field = AccessTools.Field(slType, "ShouldDrawMusicVis");
                        bool val = !(bool)field.GetValue(null);
                        field.SetValue(null, val);
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            val ? "On" : "Off"));
                        break;
                    }
                    case 23: // Sound Enabled (inverted)
                    {
                        var mmType = AccessTools.TypeByName("Hacknet.MusicManager");
                        bool muted = (bool)AccessTools.Field(mmType, "isMuted").GetValue(null);
                        AccessTools.Method(mmType, "setIsMuted")
                            .Invoke(null, new object[] { !muted });
                        Plugin.Announce(Loc.Get("settings.toggled", ctrl.Label,
                            muted ? "On" : "Off"));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"ToggleCheckbox failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the item count and current index for a list control.
        /// </summary>
        private static bool GetListInfo(SettingsControl ctrl, out string[] items,
            out int currentIndex, out string fieldName)
        {
            items = null;
            currentIndex = 0;
            fieldName = null;

            try
            {
                if (_menuInstance == null) return false;
                var type = _menuInstance.GetType();

                switch (ctrl.GuiId)
                {
                    case 10: // Resolution
                        items = AccessTools.Field(type, "resolutions")
                            .GetValue(_menuInstance) as string[];
                        currentIndex = (int)AccessTools.Field(type, "currentResIndex")
                            .GetValue(_menuInstance);
                        fieldName = "currentResIndex";
                        return items != null;

                    case 1013: // Language
                        items = AccessTools.Field(type, "localeNames")
                            .GetValue(_menuInstance) as string[];
                        currentIndex = (int)AccessTools.Field(type, "currentLocaleIndex")
                            .GetValue(_menuInstance);
                        fieldName = "currentLocaleIndex";
                        return items != null;

                    case 25: // Text Size
                        items = AccessTools.Field(type, "fontConfigs")
                            .GetValue(_menuInstance) as string[];
                        currentIndex = (int)AccessTools.Field(type, "currentFontIndex")
                            .GetValue(_menuInstance);
                        fieldName = "currentFontIndex";
                        return items != null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"GetListInfo failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Set a list's selected index and trigger game-side effects.
        /// </summary>
        private static void SetListIndex(SettingsControl ctrl, int newIndex)
        {
            try
            {
                if (_menuInstance == null) return;
                var type = _menuInstance.GetType();

                switch (ctrl.GuiId)
                {
                    case 10: // Resolution
                        AccessTools.Field(type, "currentResIndex")
                            .SetValue(_menuInstance, newIndex);
                        AccessTools.Field(type, "resolutionChanged")
                            .SetValue(_menuInstance, true);
                        var stlType = AccessTools.TypeByName("Hacknet.Gui.SelectableTextList");
                        AccessTools.Field(stlType, "wasActivated").SetValue(null, true);
                        break;

                    case 1013: // Language
                        AccessTools.Field(type, "currentLocaleIndex")
                            .SetValue(_menuInstance, newIndex);
                        // Trigger locale activation
                        var laType = AccessTools.TypeByName("Hacknet.Localization.LocaleActivator");
                        var langs = AccessTools.Field(laType, "SupportedLanguages")
                            .GetValue(null) as System.Collections.IList;
                        if (langs != null && newIndex >= 0 && newIndex < langs.Count)
                        {
                            var lang = langs[newIndex];
                            string code = (string)AccessTools.Field(lang.GetType(), "Code")
                                .GetValue(lang);
                            AccessTools.Method(laType, "ActivateLocale",
                                new[] { typeof(string) })
                                .Invoke(null, new object[] { code });
                            var settingsType = AccessTools.TypeByName("Hacknet.Settings");
                            AccessTools.Field(settingsType, "ActiveLocale")
                                .SetValue(null, code);
                        }
                        break;

                    case 25: // Text Size
                        AccessTools.Field(type, "currentFontIndex")
                            .SetValue(_menuInstance, newIndex);
                        break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"SetListIndex failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process keyboard input for settings menu.
        /// Called from Plugin.ProcessInput().
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!_menuAnnounced || _frameCount < 3) return;

            var ks = currentState;

            bool up = ks.IsKeyDown(Keys.Up) && _prevKeyState.IsKeyUp(Keys.Up);
            bool down = ks.IsKeyDown(Keys.Down) && _prevKeyState.IsKeyUp(Keys.Down);
            bool left = ks.IsKeyDown(Keys.Left) && _prevKeyState.IsKeyUp(Keys.Left);
            bool right = ks.IsKeyDown(Keys.Right) && _prevKeyState.IsKeyUp(Keys.Right);
            bool enter = ks.IsKeyDown(Keys.Enter) && _prevKeyState.IsKeyUp(Keys.Enter);
            bool space = ks.IsKeyDown(Keys.Space) && _prevKeyState.IsKeyUp(Keys.Space);
            bool escape = ks.IsKeyDown(Keys.Escape) && _prevKeyState.IsKeyUp(Keys.Escape);

            _prevKeyState = ks;

            switch (_state)
            {
                case SettingsState.ControlList:
                    HandleControlList(up, down, left, right, enter, space, escape);
                    break;
                case SettingsState.ListSelect:
                    HandleListSelect(up, down, enter, escape);
                    break;
                case SettingsState.SliderAdjust:
                    HandleSliderAdjust(left, right, enter, escape);
                    break;
            }
        }

        private static void HandleControlList(bool up, bool down, bool left, bool right,
            bool enter, bool space, bool escape)
        {
            if (_controls.Count == 0) return;

            if (up)
            {
                if (_focusIndex > 0) _focusIndex--;
                AnnounceFocused();
            }
            else if (down)
            {
                if (_focusIndex < _controls.Count - 1) _focusIndex++;
                AnnounceFocused();
            }
            else if (enter || space)
            {
                var ctrl = _controls[_focusIndex];
                switch (ctrl.Type)
                {
                    case ControlType.Checkbox:
                        ToggleCheckbox(ctrl);
                        break;

                    case ControlType.List:
                        if (GetListInfo(ctrl, out string[] items, out int curIdx, out _))
                        {
                            _state = SettingsState.ListSelect;
                            _listSubIndex = curIdx;
                            Plugin.Announce(Loc.Get("settings.listMode", ctrl.Label)
                                + " " + Loc.Get("settings.listItem",
                                    _listSubIndex + 1, items.Length, items[_listSubIndex]));
                        }
                        break;

                    case ControlType.Slider:
                        _state = SettingsState.SliderAdjust;
                        Plugin.Announce(Loc.Get("settings.sliderMode", ctrl.Label));
                        break;

                    case ControlType.Button:
                        if (ctrl.GuiId == 999)
                        {
                            // Back — save and exit
                            var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                            AccessTools.Method(slType, "writeStatusFile")
                                ?.Invoke(null, null);
                            var exitMethod = AccessTools.Method(
                                _menuInstance.GetType(), "ExitScreen");
                            if (exitMethod != null)
                            {
                                _menuAnnounced = false;
                                exitMethod.Invoke(_menuInstance, null);
                                AccessStateManager.Exit(AccessStateManager.State.OptionsMenu);
                                MainMenuPatches.Reset();
                                Plugin.Announce(Loc.Get("menu.main"), false);
                            }
                        }
                        else if (ctrl.GuiId == 9907)
                        {
                            // Apply Changes
                            if (_menuInstance != null)
                            {
                                AccessTools.Field(_menuInstance.GetType(), "needsApply")
                                    .SetValue(_menuInstance, true);
                                Plugin.Announce(Loc.Get("settings.applied"));
                            }
                        }
                        break;
                }
            }
            else if (escape)
            {
                // Save and exit
                var slType = AccessTools.TypeByName("Hacknet.SettingsLoader");
                AccessTools.Method(slType, "writeStatusFile")?.Invoke(null, null);
                var exitMethod = AccessTools.Method(
                    _menuInstance.GetType(), "ExitScreen");
                if (exitMethod != null)
                {
                    _menuAnnounced = false;
                    exitMethod.Invoke(_menuInstance, null);
                    AccessStateManager.Exit(AccessStateManager.State.OptionsMenu);
                    MainMenuPatches.Reset();
                    Plugin.Announce(Loc.Get("menu.main"), false);
                }
            }
            else if (left || right)
            {
                // Quick adjust for slider/list without entering sub-mode
                var ctrl = _controls[_focusIndex];
                if (ctrl.Type == ControlType.Slider)
                {
                    AdjustVolume(right ? 0.05f : -0.05f);
                }
            }
        }

        private static void HandleListSelect(bool up, bool down, bool enter, bool escape)
        {
            if (_focusIndex >= _controls.Count) return;
            var ctrl = _controls[_focusIndex];

            if (!GetListInfo(ctrl, out string[] items, out _, out _))
            {
                _state = SettingsState.ControlList;
                return;
            }

            if (up)
            {
                if (_listSubIndex > 0) _listSubIndex--;
                Plugin.Announce(Loc.Get("settings.listItem",
                    _listSubIndex + 1, items.Length, items[_listSubIndex]));
            }
            else if (down)
            {
                if (_listSubIndex < items.Length - 1) _listSubIndex++;
                Plugin.Announce(Loc.Get("settings.listItem",
                    _listSubIndex + 1, items.Length, items[_listSubIndex]));
            }
            else if (enter)
            {
                SetListIndex(ctrl, _listSubIndex);
                _state = SettingsState.ControlList;
                Plugin.Announce(Loc.Get("settings.selected", ctrl.Label, items[_listSubIndex]));
            }
            else if (escape)
            {
                _state = SettingsState.ControlList;
                AnnounceFocused();
            }
        }

        private static void HandleSliderAdjust(bool left, bool right, bool enter, bool escape)
        {
            if (left)
                AdjustVolume(-0.05f);
            else if (right)
                AdjustVolume(0.05f);
            else if (enter || escape)
            {
                _state = SettingsState.ControlList;
                AnnounceFocused();
            }
        }

        /// <summary>
        /// Adjust music volume by a delta and announce.
        /// </summary>
        private static void AdjustVolume(float delta)
        {
            try
            {
                var mmType = AccessTools.TypeByName("Hacknet.MusicManager");
                float vol = (float)AccessTools.Method(mmType, "getVolume").Invoke(null, null);
                vol = Math.Max(0f, Math.Min(1f, vol + delta));
                AccessTools.Method(mmType, "setVolume", new[] { typeof(float) })
                    .Invoke(null, new object[] { vol });
                Plugin.Announce(Loc.Get("settings.volume", (int)(vol * 100)));
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "OptionsMenu",
                    $"AdjustVolume failed: {ex.Message}");
            }
        }
    }
}
