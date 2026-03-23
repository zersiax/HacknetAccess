using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for terminal output — the core gameplay loop.
    /// OS.write() is the main output path for commands.
    /// Terminal.writeLine() catches direct writes (tutorial, etc.) that bypass OS.write.
    /// F2 re-reads last terminal lines.
    /// Polls terminal prompt for changes during getString/login mode.
    /// </summary>
    [HarmonyPatch]
    internal static class TerminalPatches
    {
        private static readonly List<string> _recentOutput = new List<string>();
        private static string _lastCommand;
        private static readonly StringBuilder _singleLineBuffer = new StringBuilder();
        private const int MaxRecentLines = 100;
        private static bool _inOSWrite;
        private static int _recentOutputIndex = -1;

        // Word navigation within a line
        private static int _wordIndex = -1;
        private static string[] _currentWords;

        /// <summary>
        /// Tracks what was last navigated: line (Ctrl+Up/Down) or word (Ctrl+Left/Right).
        /// Used by Ctrl+C to decide what to copy.
        /// </summary>
        public enum NavMode { None, Line, Word }
        public static NavMode LastNavMode { get; private set; } = NavMode.None;

        // Prompt change detection (for login, getString)
        private static string _lastPrompt;
        private static bool _promptFieldsInit;
        private static FieldInfo _currentInstanceField;
        private static FieldInfo _terminalField;
        private static FieldInfo _promptField;
        private static FieldInfo _preventingField;
        private static FieldInfo _maskingTextField;

        /// <summary>
        /// Wraps OS.write(string) — announce full output, flag to prevent duplicate from writeLine.
        /// </summary>
        [HarmonyPatch]
        static class OSWritePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.OS"), "write",
                    new[] { typeof(string) });
            }

            static void Prefix()
            {
                _inOSWrite = true;
            }

            static void Postfix(string text)
            {
                _inOSWrite = false;

                if (string.IsNullOrEmpty(text)) return;

                FlushSingleLineBuffer();

                string clean = CleanText(text);
                if (string.IsNullOrWhiteSpace(clean)) return;

                // During tab completion, count matches but don't announce individually
                if (TabCompletePatch.IsInTabComplete)
                {
                    _tabMatchCount++;
                    TrackOutput(clean);
                    return;
                }

                TrackOutput(clean);
                Plugin.Announce(clean, false);
            }
        }

        /// <summary>
        /// Postfix on OS.writeSingle(string) — buffer partial output.
        /// </summary>
        [HarmonyPatch]
        static class OSWriteSinglePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.OS"), "writeSingle",
                    new[] { typeof(string) });
            }

            static void Postfix(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                _singleLineBuffer.Append(text);
            }
        }

        /// <summary>
        /// Postfix on Terminal.writeLine(string) — catches direct writes
        /// (tutorial text, etc.) that bypass OS.write.
        /// Skipped when called from within OS.write to avoid duplicates.
        /// </summary>
        [HarmonyPatch]
        static class TerminalWriteLinePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Terminal"), "writeLine",
                    new[] { typeof(string) });
            }

            static void Postfix(string text)
            {
                // Skip if OS.write is handling this (avoids duplicates)
                if (_inOSWrite) return;

                if (string.IsNullOrEmpty(text)) return;

                string clean = CleanText(text);
                if (string.IsNullOrWhiteSpace(clean)) return;

                // Skip single-character fragments (visual effects, prompt chars)
                if (clean.Length <= 1) return;

                TrackOutput(clean);
                Plugin.Announce(clean, false);
            }
        }

        /// <summary>
        /// Patches doTabComplete to announce completion results.
        /// Prefix captures currentLine before, postfix compares after to determine what happened.
        /// </summary>
        [HarmonyPatch]
        static class TabCompletePatch
        {
            private static string _lineBeforeTab;
            private static bool _inTabComplete;

            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Terminal"), "doTabComplete");
            }

            static void Prefix(string ___currentLine)
            {
                _lineBeforeTab = ___currentLine;
                _inTabComplete = true;
            }

            static void Postfix(string ___currentLine, object __instance)
            {
                _inTabComplete = false;

                if (_lineBeforeTab == null) return;

                // _tabMatchCount includes the prompt echo line, so actual matches = count - 1
                int matchCount = (_tabMatchCount > 1) ? _tabMatchCount - 1 : 0;

                if (_lineBeforeTab == ___currentLine && matchCount == 0)
                {
                    // No change, no writes — no matches found
                    Plugin.Announce(Loc.Get("tab.noMatch"));
                }
                else if (matchCount == 0 && _lineBeforeTab != ___currentLine)
                {
                    // Line changed with no os.write — single match, auto-completed
                    Plugin.Announce(Loc.Get("tab.completed", ___currentLine));
                }
                else if (matchCount > 1)
                {
                    // Multiple matches listed in terminal, common prefix filled
                    Plugin.Announce(Loc.Get("tab.multiple", matchCount, ___currentLine));
                }

                _tabMatchCount = 0;
            }

            /// <summary>
            /// Returns true if currently inside doTabComplete (used to count OS.write calls).
            /// </summary>
            public static bool IsInTabComplete => _inTabComplete;
        }

        private static int _tabMatchCount;

        /// <summary>
        /// Prefix on Terminal.executeLine() — capture command before it clears,
        /// and flush any pending writeSingle buffer.
        /// </summary>
        [HarmonyPatch]
        static class TerminalExecuteLinePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.Terminal"), "executeLine");
            }

            static void Prefix(string ___currentLine)
            {
                FlushSingleLineBuffer();

                if (!string.IsNullOrEmpty(___currentLine))
                {
                    _lastCommand = ___currentLine;
                    _recentOutputIndex = -1; // Reset nav position on new command
                    DebugLogger.Log(LogCategory.Handler, "Terminal", $"Command: {___currentLine}");
                }
            }
        }

        /// <summary>
        /// Clean text for screen reader output.
        /// Strips newlines, #marker# formatting, and leading/trailing whitespace.
        /// </summary>
        private static string CleanText(string text)
        {
            // Strip #markers# used by tutorial for highlighting
            text = Regex.Replace(text, "#([^#]*)#", "$1");
            // Replace newlines with spaces
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            // Collapse multiple spaces
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        /// <summary>
        /// Flush buffered writeSingle text and announce it.
        /// </summary>
        private static void FlushSingleLineBuffer()
        {
            if (_singleLineBuffer.Length == 0) return;

            string text = _singleLineBuffer.ToString();
            _singleLineBuffer.Clear();
            string clean = CleanText(text);
            if (string.IsNullOrWhiteSpace(clean)) return;

            TrackOutput(clean);
            Plugin.Announce(clean, false);
        }

        /// <summary>
        /// Track output for F2 re-read.
        /// </summary>
        private static void TrackOutput(string text)
        {
            _recentOutput.Add(text);
            if (_recentOutput.Count > MaxRecentLines)
            {
                _recentOutput.RemoveAt(0);
            }
        }

        /// <summary>
        /// Process F2 shortcut and poll for terminal prompt changes.
        /// Called from Plugin.ProcessInput().
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            // Poll for prompt changes (login, getString)
            CheckPromptChange();

            if (Plugin.IsKeyPressed(Keys.F2, currentState))
            {
                FlushSingleLineBuffer();

                DebugLogger.LogInput("F2", "Re-read terminal");
                if (_recentOutput.Count == 0)
                {
                    Plugin.Announce(Loc.Get("terminal.empty"));
                    return;
                }

                var sb = new StringBuilder();
                foreach (string line in _recentOutput)
                {
                    sb.AppendLine(line);
                }
                Plugin.Announce(sb.ToString());
            }

            // Ctrl+Up/Down — navigate terminal output line by line
            // Ctrl+Left/Right — navigate words within the current line
            bool ctrl = currentState.IsKeyDown(Keys.LeftControl)
                     || currentState.IsKeyDown(Keys.RightControl);
            if (ctrl && _recentOutput.Count > 0
                && !DisplayModulePatches.DisplayHasFocus
                && !NetworkMapPatches.HasFocus
                && !NotesPatches.HasFocus)
            {
                if (Plugin.IsKeyPressed(Keys.Up, currentState))
                {
                    if (_recentOutputIndex < 0)
                        _recentOutputIndex = _recentOutput.Count - 1;
                    else if (_recentOutputIndex > 0)
                        _recentOutputIndex--;
                    ResetWordNav();
                    LastNavMode = NavMode.Line;
                    Plugin.Announce(Loc.Get("terminal.line",
                        _recentOutputIndex + 1, _recentOutput.Count,
                        _recentOutput[_recentOutputIndex]));
                }
                else if (Plugin.IsKeyPressed(Keys.Down, currentState))
                {
                    if (_recentOutputIndex < 0)
                        _recentOutputIndex = 0;
                    else if (_recentOutputIndex < _recentOutput.Count - 1)
                        _recentOutputIndex++;
                    ResetWordNav();
                    LastNavMode = NavMode.Line;
                    Plugin.Announce(Loc.Get("terminal.line",
                        _recentOutputIndex + 1, _recentOutput.Count,
                        _recentOutput[_recentOutputIndex]));
                }
                else if (Plugin.IsKeyPressed(Keys.Left, currentState)
                    && _recentOutputIndex >= 0)
                {
                    EnsureWordNav();
                    if (_currentWords != null && _currentWords.Length > 0 && _wordIndex > 0)
                    {
                        _wordIndex--;
                        LastNavMode = NavMode.Word;
                        Plugin.Announce(Loc.Get("terminal.word",
                            _wordIndex + 1, _currentWords.Length,
                            _currentWords[_wordIndex]));
                    }
                }
                else if (Plugin.IsKeyPressed(Keys.Right, currentState)
                    && _recentOutputIndex >= 0)
                {
                    EnsureWordNav();
                    if (_currentWords != null && _currentWords.Length > 0
                        && _wordIndex < _currentWords.Length - 1)
                    {
                        _wordIndex++;
                        LastNavMode = NavMode.Word;
                        Plugin.Announce(Loc.Get("terminal.word",
                            _wordIndex + 1, _currentWords.Length,
                            _currentWords[_wordIndex]));
                    }
                }
            }
        }

        /// <summary>
        /// Returns the text that Ctrl+C should copy, based on last nav mode.
        /// Line mode: current line. Word mode: current word. None: null.
        /// </summary>
        public static string GetCopyText()
        {
            if (LastNavMode == NavMode.Line
                && _recentOutputIndex >= 0 && _recentOutputIndex < _recentOutput.Count)
                return _recentOutput[_recentOutputIndex];
            if (LastNavMode == NavMode.Word
                && _currentWords != null && _wordIndex >= 0 && _wordIndex < _currentWords.Length)
                return _currentWords[_wordIndex];
            return null;
        }

        private static void ResetWordNav()
        {
            _wordIndex = -1;
            _currentWords = null;
        }

        private static void EnsureWordNav()
        {
            if (_currentWords != null) return;
            if (_recentOutputIndex < 0 || _recentOutputIndex >= _recentOutput.Count) return;
            _currentWords = _recentOutput[_recentOutputIndex]
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            _wordIndex = 0;
        }

        /// <summary>
        /// Returns true if the terminal is in input mode (getString/login).
        /// Used by other patches to check if the terminal is capturing text.
        /// </summary>
        public static bool IsTerminalInputMode()
        {
            InitPromptFields();
            if (_preventingField == null) return false;

            try
            {
                var os = _currentInstanceField?.GetValue(null);
                if (os == null) return false;

                var terminal = _terminalField?.GetValue(os);
                if (terminal == null) return false;

                return (bool)(_preventingField.GetValue(terminal) ?? false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initialize cached FieldInfo for prompt polling.
        /// </summary>
        private static void InitPromptFields()
        {
            if (_promptFieldsInit) return;
            _promptFieldsInit = true;

            var osType = AccessTools.TypeByName("Hacknet.OS");
            _currentInstanceField = AccessTools.Field(osType, "currentInstance");
            _terminalField = AccessTools.Field(osType, "terminal");

            var termType = AccessTools.TypeByName("Hacknet.Terminal");
            _promptField = AccessTools.Field(termType, "prompt");
            _preventingField = AccessTools.Field(termType, "preventingExecution");

            _maskingTextField = AccessTools.Field(
                AccessTools.TypeByName("Hacknet.Gui.TextBox"), "MaskingText");
        }

        /// <summary>
        /// Detect terminal prompt changes during getString/login mode
        /// and announce them to the screen reader.
        /// </summary>
        private static void CheckPromptChange()
        {
            InitPromptFields();
            if (_promptField == null || _preventingField == null) return;

            try
            {
                var os = _currentInstanceField?.GetValue(null);
                if (os == null) return;

                var terminal = _terminalField?.GetValue(os);
                if (terminal == null) return;

                string prompt = (string)_promptField.GetValue(terminal);
                bool preventing = (bool)(_preventingField.GetValue(terminal) ?? false);

                if (preventing && prompt != _lastPrompt && !string.IsNullOrEmpty(prompt))
                {
                    _lastPrompt = prompt;

                    bool masking = (bool)(_maskingTextField?.GetValue(null) ?? false);
                    if (masking)
                    {
                        Plugin.Announce(Loc.Get("terminal.promptPassword", prompt), false);
                    }
                    else
                    {
                        Plugin.Announce(Loc.Get("terminal.promptInput", prompt), false);
                    }
                    DebugLogger.Log(LogCategory.Handler, "Terminal",
                        $"Prompt changed: {prompt} (masked={masking})");
                }
                else
                {
                    // Update tracking without announcing (normal prompt changes)
                    _lastPrompt = prompt;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Terminal",
                    $"CheckPromptChange failed: {ex.Message}");
            }
        }
    }
}
