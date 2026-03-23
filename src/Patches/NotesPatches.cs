using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for Notes accessibility.
    /// F7 reads all notes and enters focus mode. Up/Down navigates notes.
    /// Escape exits focus. Ctrl+Shift+W closes notes.
    /// Announces when notes are added.
    /// </summary>
    [HarmonyPatch]
    internal static class NotesPatches
    {
        private static List<string> _notes = new List<string>();
        private static int _noteIndex;
        private static bool _notesHaveFocus;

        /// <summary>
        /// Whether notes focus mode is active.
        /// Used by other patches to avoid key conflicts.
        /// </summary>
        public static bool HasFocus => _notesHaveFocus;

        /// <summary>
        /// Postfix on NotesExe.AddNote — announce when a note is added.
        /// </summary>
        [HarmonyPatch]
        static class AddNotePatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.NotesExe"), "AddNote",
                    new[] { typeof(string) });
            }

            static void Postfix(string note)
            {
                if (!string.IsNullOrEmpty(note))
                {
                    Plugin.Announce(Loc.Get("notes.added", note), false);
                }
            }
        }

        /// <summary>
        /// Close the running NotesExe and instantly free RAM.
        /// Bypasses the visual fade-out (useless for screen reader users)
        /// to ensure RAM is freed immediately.
        /// </summary>
        private static void CloseNotes()
        {
            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return;

                var exes = AccessTools.Field(osType, "exes")?.GetValue(os) as IList;
                if (exes == null) return;

                var notesExeType = AccessTools.TypeByName("Hacknet.NotesExe");
                var exeModType = AccessTools.TypeByName("Hacknet.ExeModule");

                foreach (var exe in exes)
                {
                    if (exe.GetType() == notesExeType)
                    {
                        // Call Completed() and RemoveReopnener() like the game does
                        AccessTools.Method(notesExeType, "Completed")?.Invoke(exe, null);
                        AccessTools.Method(notesExeType, "RemoveReopnener")?.Invoke(exe, null);

                        // Instant kill — bypass visual fade, free RAM immediately
                        AccessTools.Field(exeModType, "isExiting")?.SetValue(exe, true);
                        AccessTools.Field(exeModType, "needsRemoval")?.SetValue(exe, true);
                        AccessTools.Field(exeModType, "ramCost")?.SetValue(exe, 0);
                        AccessTools.Field(exeModType, "fade")?.SetValue(exe, 0f);

                        _notes.Clear();
                        Plugin.Announce(Loc.Get("notes.closed"), false);
                        return;
                    }
                }

                Plugin.Announce(Loc.Get("notes.notRunning"), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Notes",
                    $"CloseNotes failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all notes from the running NotesExe instance.
        /// </summary>
        private static bool RefreshNotes()
        {
            _notes.Clear();
            _noteIndex = 0;

            try
            {
                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                if (os == null) return false;

                var exes = AccessTools.Field(osType, "exes")?.GetValue(os) as IList;
                if (exes == null) return false;

                var notesExeType = AccessTools.TypeByName("Hacknet.NotesExe");

                foreach (var exe in exes)
                {
                    if (exe.GetType() == notesExeType)
                    {
                        var notesList = AccessTools.Field(notesExeType, "notes")
                            ?.GetValue(exe) as List<string>;
                        if (notesList != null)
                        {
                            _notes.AddRange(notesList);
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Notes",
                    $"RefreshNotes failed: {ex.Message}");
            }

            return false;
        }


        /// <summary>
        /// Process F7 to read notes and enter focus mode.
        /// Up/Down navigates notes. Escape exits focus.
        /// Ctrl+Shift+W closes notes.
        /// Called from Plugin.ProcessInput().
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            bool ctrl = currentState.IsKeyDown(Keys.LeftControl) || currentState.IsKeyDown(Keys.RightControl);
            bool shift = currentState.IsKeyDown(Keys.LeftShift) || currentState.IsKeyDown(Keys.RightShift);

            // F7 — read all notes and enter focus mode
            if (Plugin.IsKeyPressed(Keys.F7, currentState))
            {
                DebugLogger.LogInput("F7", "Read notes");

                if (!RefreshNotes() || _notes.Count == 0)
                {
                    _notesHaveFocus = false;
                    Plugin.Announce(Loc.Get("notes.empty"));
                    return;
                }

                // Enter notes focus, clear other focus modes
                _notesHaveFocus = true;
                DisplayModulePatches.DisplayHasFocus = false;
                NetworkMapPatches.Reset();

                var sb = new StringBuilder();
                sb.AppendLine(Loc.Get("notes.header", _notes.Count));
                for (int i = 0; i < _notes.Count; i++)
                {
                    string clean = _notes[i].Replace("\r\n", " ").Replace("\n", " ");
                    sb.AppendLine($"{i + 1}. {clean}");
                }
                Plugin.Announce(sb.ToString().Trim());
                return;
            }

            // Ctrl+Shift+W to close notes
            if (ctrl && shift && Plugin.IsKeyPressed(Keys.W, currentState))
            {
                _notesHaveFocus = false;
                CloseNotes();
                return;
            }

            // Navigation when notes have focus — bare Up/Down
            if (_notesHaveFocus)
            {
                if (Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _notesHaveFocus = false;
                    Plugin.Announce(Loc.Get("display.terminalFocused"));
                    return;
                }

                if (Plugin.IsKeyPressed(Keys.Up, currentState))
                {
                    if (_notes.Count == 0) RefreshNotes();
                    if (_notes.Count == 0) return;

                    if (_noteIndex > 0) _noteIndex--;
                    string clean = _notes[_noteIndex].Replace("\r\n", " ").Replace("\n", " ");
                    Plugin.Announce(Loc.Get("notes.item", _noteIndex + 1, _notes.Count, clean));
                }
                else if (Plugin.IsKeyPressed(Keys.Down, currentState))
                {
                    if (_notes.Count == 0) RefreshNotes();
                    if (_notes.Count == 0) return;

                    if (_noteIndex < _notes.Count - 1) _noteIndex++;
                    string clean = _notes[_noteIndex].Replace("\r\n", " ").Replace("\n", " ");
                    Plugin.Announce(Loc.Get("notes.item", _noteIndex + 1, _notes.Count, clean));
                }
            }
        }
    }
}
