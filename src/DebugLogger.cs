namespace HacknetAccess
{
    /// <summary>
    /// Centralized debug logging with categories.
    /// All logging goes through here so it can be filtered and controlled.
    ///
    /// Usage:
    ///   DebugLogger.Log(LogCategory.Input, "Tab pressed");
    ///   DebugLogger.Log(LogCategory.State, "Menu opened");
    ///   DebugLogger.Log(LogCategory.Handler, "MainMenuPatches", "Focus changed to button 3");
    ///
    /// Categories help when reading logs:
    ///   [SR] Screen reader output — what the user hears
    ///   [INPUT] Key presses and input events
    ///   [STATE] Screen/menu state changes
    ///   [HANDLER] Patch decisions and actions
    ///   [GAME] Values read from game (positions, stats, etc.)
    /// </summary>
    public static class DebugLogger
    {
        /// <summary>
        /// Log a debug message with category.
        /// Only logs when Plugin.DebugMode is true.
        /// </summary>
        public static void Log(LogCategory category, string message)
        {
            if (!Plugin.DebugMode) return;

            string prefix = GetPrefix(category);
            Plugin.Instance.Log.LogInfo($"{prefix} {message}");
        }

        /// <summary>
        /// Log a debug message with category and source.
        /// Useful for identifying which patch class logged.
        /// </summary>
        public static void Log(LogCategory category, string source, string message)
        {
            if (!Plugin.DebugMode) return;

            string prefix = GetPrefix(category);
            Plugin.Instance.Log.LogInfo($"{prefix} [{source}] {message}");
        }

        /// <summary>
        /// Log screen reader output. Called by ScreenReader wrapper.
        /// </summary>
        public static void LogScreenReader(string text)
        {
            if (!Plugin.DebugMode) return;

            Plugin.Instance.Log.LogInfo($"[SR] {text}");
        }

        /// <summary>
        /// Log a key press event.
        /// </summary>
        public static void LogInput(string keyName, string action = null)
        {
            if (!Plugin.DebugMode) return;

            string msg = action != null
                ? $"{keyName} -> {action}"
                : keyName;
            Plugin.Instance.Log.LogInfo($"[INPUT] {msg}");
        }

        /// <summary>
        /// Log a state change (screen opened/closed, mode changed).
        /// </summary>
        public static void LogState(string description)
        {
            if (!Plugin.DebugMode) return;

            Plugin.Instance.Log.LogInfo($"[STATE] {description}");
        }

        /// <summary>
        /// Log a game value read for debugging.
        /// </summary>
        public static void LogGameValue(string name, object value)
        {
            if (!Plugin.DebugMode) return;

            Plugin.Instance.Log.LogInfo($"[GAME] {name} = {value}");
        }

        private static string GetPrefix(LogCategory category)
        {
            return category switch
            {
                LogCategory.ScreenReader => "[SR]",
                LogCategory.Input => "[INPUT]",
                LogCategory.State => "[STATE]",
                LogCategory.Handler => "[HANDLER]",
                LogCategory.Game => "[GAME]",
                _ => "[DEBUG]"
            };
        }
    }

    /// <summary>
    /// Categories for debug logging.
    /// </summary>
    public enum LogCategory
    {
        /// <summary>What the screen reader announces</summary>
        ScreenReader,

        /// <summary>Key presses and input events</summary>
        Input,

        /// <summary>Screen/menu state changes</summary>
        State,

        /// <summary>Patch decisions and processing</summary>
        Handler,

        /// <summary>Values read from the game</summary>
        Game
    }
}
