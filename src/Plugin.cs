using System;
using System.Reflection;
using BepInEx;
using BepInEx.Hacknet;
using HarmonyLib;
using HacknetAccess.Patches;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess
{
    /// <summary>
    /// Main plugin entry point for HacknetAccess.
    /// Handles initialization, F-key input polling, and shutdown.
    /// </summary>
    [BepInPlugin("com.hacknetaccess.plugin", "HacknetAccess", "0.1.0")]
    public class Plugin : HacknetPlugin
    {
        /// <summary>
        /// Singleton instance for global access.
        /// </summary>
        public static Plugin Instance { get; private set; }

        /// <summary>
        /// When true, debug logging is active. Toggle with F12.
        /// </summary>
        public static bool DebugMode { get; private set; } = true;

        private static KeyboardState _previousKeyState;
        private static string _lastAnnouncement;

        public override bool Load()
        {
            Instance = this;
            Log.LogInfo("HacknetAccess v0.1.0 loading...");

            ScreenReader.Init();
            ScreenReader.Output(Loc.Get("mod.loaded"));

            _previousKeyState = Keyboard.GetState();

            HarmonyInstance.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo("HacknetAccess v0.1.0 loaded successfully.");
            return true;
        }

        /// <summary>
        /// Called every frame by Pathfinder. Handles mod shortcut keys.
        /// </summary>
        public override bool Unload()
        {
            Shutdown();
            return true;
        }

        /// <summary>
        /// Process mod input keys. Call from a Harmony patch on OS.Update or Game1.Update.
        /// </summary>
        public static void ProcessInput()
        {
            var currentKeyState = Keyboard.GetState();
            try
            {

            // F1 — Help
            if (IsKeyPressed(Keys.F1, currentKeyState))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(Loc.Get("help.title"));
                sb.AppendLine(Loc.Get("help.f1"));
                sb.AppendLine(Loc.Get("help.f2"));
                sb.AppendLine(Loc.Get("help.f3"));
                sb.AppendLine(Loc.Get("help.f4"));
                sb.AppendLine(Loc.Get("help.f5"));
                sb.AppendLine(Loc.Get("help.f6"));
                sb.AppendLine(Loc.Get("help.f7"));
                sb.AppendLine(Loc.Get("help.f9"));
                sb.AppendLine(Loc.Get("help.f10"));
                sb.AppendLine(Loc.Get("help.f12"));
                sb.AppendLine(Loc.Get("help.ctrlr"));
                sb.AppendLine(Loc.Get("help.ctrlc"));
                sb.AppendLine(Loc.Get("help.ctrlupdown"));
                sb.AppendLine(Loc.Get("help.ctrlleftright"));
                sb.AppendLine(Loc.Get("help.ctrlhomeend"));
                sb.AppendLine(Loc.Get("help.ctrlo"));
                sb.AppendLine(Loc.Get("help.ctrlt"));
                sb.AppendLine(Loc.Get("help.ctrlw"));
                sb.AppendLine(Loc.Get("help.ctrlenter"));
                Announce(sb.ToString());
                DebugLogger.LogInput("F1", "Help");
            }

            // F9 — Save session
            if (IsKeyPressed(Keys.F9, currentKeyState))
            {
                try
                {
                    var osType = AccessTools.TypeByName("Hacknet.OS");
                    var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                    if (os != null)
                    {
                        var saveMethod = AccessTools.Method(osType, "saveGame");
                        saveMethod?.Invoke(os, null);
                        Announce(Loc.Get("game.saved"));
                        DebugLogger.LogInput("F9", "Save session");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Game, "Plugin", $"F9 save failed: {ex.Message}");
                }
            }

            // F10 — Open settings
            if (IsKeyPressed(Keys.F10, currentKeyState))
            {
                try
                {
                    var osType = AccessTools.TypeByName("Hacknet.OS");
                    var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                    if (os != null)
                    {
                        // Check TraceDangerSequence.IsActive — game blocks settings during trace
                        var traceField = AccessTools.Field(osType, "TraceDangerSequence");
                        var traceSeq = traceField?.GetValue(os);
                        if (traceSeq != null)
                        {
                            var isActiveProp = AccessTools.Property(traceSeq.GetType(), "IsActive");
                            bool isActive = isActiveProp != null && (bool)isActiveProp.GetValue(traceSeq, null);
                            if (isActive)
                            {
                                Announce(Loc.Get("settings.unavailable"));
                                return;
                            }
                        }

                        // Save before opening settings (same as game behavior)
                        var saveMethod = AccessTools.Method(osType, "saveGame");
                        saveMethod?.Invoke(os, null);

                        // Open OptionsMenu via ScreenManager
                        var optionsType = AccessTools.TypeByName("Hacknet.OptionsMenu");
                        var ctor = AccessTools.Constructor(optionsType, new Type[] { typeof(bool) });
                        var optionsMenu = ctor?.Invoke(new object[] { true });
                        if (optionsMenu == null)
                        {
                            DebugLogger.Log(LogCategory.Game, "Plugin", "F10: Failed to create OptionsMenu");
                            return;
                        }

                        var gameScreenType = AccessTools.TypeByName("Hacknet.GameScreen");
                        var screenMgrProp = AccessTools.Property(gameScreenType, "ScreenManager");
                        var screenMgr = screenMgrProp?.GetValue(os, null);
                        if (screenMgr != null)
                        {
                            var screenMgrType = screenMgr.GetType();
                            var controllingPlayerField = AccessTools.Field(screenMgrType, "controllingPlayer");
                            var controllingPlayer = controllingPlayerField?.GetValue(screenMgr);
                            // Use the 2-param overload: AddScreen(GameScreen, PlayerIndex?)
                            var addScreenMethod = AccessTools.Method(screenMgrType, "AddScreen",
                                new Type[] { gameScreenType, typeof(Microsoft.Xna.Framework.PlayerIndex?) });
                            addScreenMethod?.Invoke(screenMgr, new object[] { optionsMenu, controllingPlayer });
                            DebugLogger.LogInput("F10", "Open settings");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Game, "Plugin", $"F10 settings failed: {ex.Message}");
                }
            }

            // F12 — Toggle debug mode
            if (IsKeyPressed(Keys.F12, currentKeyState))
            {
                DebugMode = !DebugMode;
                string state = DebugMode ? "on" : "off";
                ScreenReader.Output(Loc.Get("debug.toggle", state));
                Instance.Log.LogInfo($"Debug mode: {state}");
            }

            // Ctrl+R — Repeat last announcement
            if (currentKeyState.IsKeyDown(Keys.LeftControl) &&
                IsKeyPressed(Keys.R, currentKeyState))
            {
                if (!string.IsNullOrEmpty(_lastAnnouncement))
                {
                    ScreenReader.Output(_lastAnnouncement);
                    DebugLogger.LogInput("Ctrl+R", "Repeat last announcement");
                }
            }

            // Ctrl+C — Copy navigated line or word to clipboard
            if (currentKeyState.IsKeyDown(Keys.LeftControl) &&
                IsKeyPressed(Keys.C, currentKeyState))
            {
                string text = TerminalPatches.GetCopyText();
                if (text != null)
                {
                    SDL2.SDL.SDL_SetClipboardText(text);
                    Announce(Loc.Get("terminal.copied", text));
                    DebugLogger.LogInput("Ctrl+C", $"Copied: {text}");
                }
            }

            // Delegate to feature-specific input handlers
            TerminalPatches.ProcessInput(currentKeyState);
            DisplayModulePatches.ProcessInput(currentKeyState);
            NetworkMapPatches.ProcessInput(currentKeyState);
            RamModulePatches.ProcessInput(currentKeyState);
            MailPatches.ProcessInput(currentKeyState);
            NotesPatches.ProcessInput(currentKeyState);
            MissionHubPatches.ProcessInput(currentKeyState);
            MissionListingPatches.ProcessInput(currentKeyState);
            MessageBoardPatches.ProcessInput(currentKeyState);
            DatabaseDaemonPatches.ProcessInput(currentKeyState);
            ExeModulePatches.ProcessInput(currentKeyState);
            OptionsMenuPatches.ProcessInput(currentKeyState);

            }
            finally
            {
                _previousKeyState = currentKeyState;
            }
        }

        /// <summary>
        /// Check if a key was just pressed (not held).
        /// </summary>
        public static bool IsKeyPressed(Keys key, KeyboardState currentState)
        {
            return currentState.IsKeyDown(key) && _previousKeyState.IsKeyUp(key);
        }

        /// <summary>
        /// Check if a key was just pressed using the current keyboard state.
        /// </summary>
        public static bool IsKeyPressed(Keys key)
        {
            return Keyboard.GetState().IsKeyDown(key) && _previousKeyState.IsKeyUp(key);
        }

        /// <summary>
        /// Store the last announcement for Ctrl+R repeat.
        /// </summary>
        public static void SetLastAnnouncement(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _lastAnnouncement = text;
        }

        /// <summary>
        /// Announce text via screen reader and track for repeat.
        /// </summary>
        public static void Announce(string text, bool interrupt = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            ScreenReader.Output(text, interrupt);
            SetLastAnnouncement(text);
            DebugLogger.LogScreenReader(text);
        }

        private void Shutdown()
        {
            ScreenReader.Shutdown();
            Log.LogInfo("HacknetAccess unloaded.");
        }
    }
}
