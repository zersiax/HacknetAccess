using BepInEx;
using BepInEx.Hacknet;
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
                sb.AppendLine(Loc.Get("help.f12"));
                sb.AppendLine(Loc.Get("help.ctrlr"));
                sb.AppendLine(Loc.Get("help.ctrlc"));
                sb.AppendLine(Loc.Get("help.ctrlupdown"));
                sb.AppendLine(Loc.Get("help.ctrlleftright"));
                sb.AppendLine(Loc.Get("help.ctrlo"));
                sb.AppendLine(Loc.Get("help.ctrlt"));
                Announce(sb.ToString());
                DebugLogger.LogInput("F1", "Help");
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

            _previousKeyState = currentKeyState;
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
