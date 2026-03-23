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
    /// Patches for AcademicDatabaseDaemon, MedicalDatabaseDaemon,
    /// and DeathRowDatabaseDaemon — search+display database UIs.
    /// Ctrl+Up/Down navigates multi-match results, Ctrl+Enter selects.
    /// </summary>
    [HarmonyPatch]
    internal static class DatabaseDaemonPatches
    {
        // Academic DB state
        private static int _lastAcademicState = -1;
        private static bool _academicActive;
        private static int _academicMatchIndex;
        private static int _academicMatchCount;
        private static List<string> _academicMatchNames = new List<string>();
        private static int _pendingButton = -1;
        private static object _academicInstance;

        // Medical DB state
        private static int _lastMedicalState = -1;
        private static bool _medicalActive;

        // DeathRow DB state
        private static int _lastDeathRowIndex = -2;
        private static bool _deathRowActive;
        private static object _deathRowInstance;

        /// <summary>
        /// Whether any database daemon is currently being drawn.
        /// Used by DisplayModulePatches to detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _academicActive || _deathRowActive || _medicalActive;

        #region Academic Database

        /// <summary>
        /// Prefix on AcademicDatabaseDaemon.draw — mark active.
        /// </summary>
        [HarmonyPatch]
        static class AcademicDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AcademicDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _academicActive = true;
                _academicInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on AcademicDatabaseDaemon.draw — detect state changes.
        /// ADDState enum: Welcome(0), Seach(1), MultiMatchSearch(2), Entry(3),
        /// PendingResult(4), EntryNotFound(5), MultipleEntriesFound(6),
        /// InfoPanel(7), EditPerson(8), EditEntry(9)
        /// </summary>
        [HarmonyPatch]
        static class AcademicDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.AcademicDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    int stateVal = (int)AccessTools.Field(type, "state").GetValue(__instance);

                    if (stateVal != _lastAcademicState)
                    {
                        _lastAcademicState = stateVal;

                        switch (stateVal)
                        {
                            case 0: // Welcome
                                Plugin.Announce(Loc.Get("db.welcome"), false);
                                break;

                            case 1: // Search
                                Plugin.Announce(Loc.Get("db.search"), false);
                                break;

                            case 3: // Entry
                                AnnounceAcademicEntry(__instance);
                                break;

                            case 5: // EntryNotFound
                                Plugin.Announce(Loc.Get("db.notFound"), false);
                                break;

                            case 6: // MultipleEntriesFound
                                BuildAcademicMatches(__instance);
                                if (_academicMatchCount > 0)
                                {
                                    Plugin.Announce(Loc.Get("db.multiMatch", _academicMatchCount), false);
                                    AnnounceCurrentMatch();
                                }
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation in sub-states
                    if (_lastAcademicState == 0 || _lastAcademicState == 3
                        || _lastAcademicState == 5 || _lastAcademicState == 6)
                        DisplayModulePatches.DaemonClaimsEscape = true;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "AcademicDB",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch Button.doButton for academic DB buttons.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class AcademicButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "DatabaseDaemon",
                        $"Activated button: {myID}");
                }
            }
        }

        /// <summary>
        /// Announce academic database entry details.
        /// </summary>
        private static void AnnounceAcademicEntry(object instance)
        {
            try
            {
                var type = instance.GetType();
                string foundFileName = (string)AccessTools.Field(type, "foundFileName")
                    .GetValue(instance);

                var searchedDegrees = AccessTools.Field(type, "searchedDegrees")
                    .GetValue(instance) as IList;

                var sb = new StringBuilder();
                sb.Append(Loc.Get("db.entry", foundFileName ?? "Unknown"));

                if (searchedDegrees != null && searchedDegrees.Count > 0)
                {
                    foreach (var degree in searchedDegrees)
                    {
                        string degreeStr = degree?.ToString() ?? "";
                        sb.Append(". ");
                        sb.Append(Loc.Get("db.degree", degreeStr));
                    }
                }

                Plugin.Announce(sb.ToString(), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "AcademicDB",
                    $"AnnounceEntry failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Build the list of matching names for multi-match state.
        /// </summary>
        private static void BuildAcademicMatches(object instance)
        {
            _academicMatchNames.Clear();
            _academicMatchCount = 0;
            _academicMatchIndex = 0;

            try
            {
                var type = instance.GetType();
                var results = AccessTools.Field(type, "searchResultsNames")
                    .GetValue(instance) as IList;
                if (results == null) return;

                foreach (var name in results)
                {
                    string nameStr = name?.ToString() ?? "";
                    _academicMatchNames.Add(nameStr);
                    _academicMatchCount++;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "AcademicDB",
                    $"BuildMatches failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce current match in multi-match list.
        /// </summary>
        private static void AnnounceCurrentMatch()
        {
            if (_academicMatchIndex < 0 || _academicMatchIndex >= _academicMatchCount) return;
            Plugin.Announce(Loc.Get("db.matchItem",
                _academicMatchIndex + 1, _academicMatchCount,
                _academicMatchNames[_academicMatchIndex]));
        }

        #endregion

        #region Medical Database

        /// <summary>
        /// Prefix on MedicalDatabaseDaemon.draw — mark active.
        /// </summary>
        [HarmonyPatch]
        static class MedicalDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MedicalDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix()
            {
                _medicalActive = true;
            }
        }

        /// <summary>
        /// Postfix on MedicalDatabaseDaemon.draw — announce state changes.
        /// MedicalDatabaseState: MainMenu(0), Search(1), Searching(2),
        /// Entry(3), Error(4), AboutScreen(5), SendReport(6),
        /// SendReportSearch(7), SendReportSending(8), SendReportComplete(9)
        /// </summary>
        [HarmonyPatch]
        static class MedicalDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MedicalDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    int stateVal = (int)AccessTools.Field(type, "state").GetValue(__instance);

                    if (stateVal != _lastMedicalState)
                    {
                        _lastMedicalState = stateVal;

                        switch (stateVal)
                        {
                            case 0: // MainMenu
                                Plugin.Announce(Loc.Get("db.welcome") + " Medical Database.", false);
                                break;
                            case 1: // Search
                                Plugin.Announce(Loc.Get("db.search"), false);
                                break;
                            case 3: // Entry
                                AnnounceMedicalEntry(__instance);
                                break;
                            case 4: // Error
                                Plugin.Announce(Loc.Get("db.notFound"), false);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "MedicalDB",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Announce medical record details.
        /// </summary>
        private static void AnnounceMedicalEntry(object instance)
        {
            try
            {
                var type = instance.GetType();
                var currentRecord = AccessTools.Field(type, "currentRecord").GetValue(instance);
                if (currentRecord == null) return;

                var recordType = currentRecord.GetType();
                string firstName = (string)AccessTools.Field(recordType, "Firstname")?.GetValue(currentRecord) ?? "";
                string lastName = (string)AccessTools.Field(recordType, "Lastname")?.GetValue(currentRecord) ?? "";
                string record = (string)AccessTools.Field(recordType, "record")?.GetValue(currentRecord) ?? "";

                string name = $"{firstName} {lastName}".Trim();
                Plugin.Announce(Loc.Get("db.entry", name) + ". " + record, false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MedicalDB",
                    $"AnnounceEntry failed: {ex.Message}");
            }
        }

        #endregion

        #region Death Row Database

        /// <summary>
        /// Prefix on DeathRowDatabaseDaemon.draw — mark active.
        /// </summary>
        [HarmonyPatch]
        static class DeathRowDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DeathRowDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _deathRowActive = true;
                _deathRowInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on DeathRowDatabaseDaemon.draw — announce selection changes.
        /// No state enum; uses SelectedIndex (-1 = title screen, 0+ = record).
        /// </summary>
        [HarmonyPatch]
        static class DeathRowDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DeathRowDatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    int selectedIndex = (int)AccessTools.Field(type, "SelectedIndex")
                        .GetValue(__instance);

                    if (selectedIndex != _lastDeathRowIndex)
                    {
                        _lastDeathRowIndex = selectedIndex;

                        if (selectedIndex < 0)
                        {
                            Plugin.Announce(Loc.Get("db.welcome") + " Death Row Database.", false);
                        }
                        else
                        {
                            AnnounceDeathRowEntry(__instance, selectedIndex);
                        }
                    }

                    // Claim Escape for internal back-navigation
                    DisplayModulePatches.DaemonClaimsEscape = true;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "DeathRowDB",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Announce death row record details.
        /// </summary>
        private static void AnnounceDeathRowEntry(object instance, int index)
        {
            try
            {
                var type = instance.GetType();
                var records = AccessTools.Field(type, "records")
                    ?.GetValue(instance) as IList;
                if (records == null) return;

                // records is loaded from LoadRecords — try to find it
                // DeathRowDatabaseDaemon stores records differently — they are
                // loaded as List<DeathRowEntry> via LoadRecords
                // The field might be on the Folder structure instead; check draw code
                // Actually the draw method uses SelectableTextList which manages its own items.
                // Let's read from the Folder files instead.
                var recordsFolder = AccessTools.Field(type, "records").GetValue(instance);
                if (recordsFolder == null) return;

                // DeathRowDatabaseDaemon.records is a Folder, entries are in its files
                var files = AccessTools.Field(recordsFolder.GetType(), "files")
                    ?.GetValue(recordsFolder) as IList;
                if (files == null || index >= files.Count) return;

                var file = files[index];
                string name = (string)AccessTools.Field(file.GetType(), "name")?.GetValue(file) ?? "";
                string data = (string)AccessTools.Field(file.GetType(), "data")?.GetValue(file) ?? "";

                // Truncate data for announcement
                if (data.Length > 500) data = data.Substring(0, 500) + "...";

                Plugin.Announce(Loc.Get("db.entry", name) + ". " + data, false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "DeathRowDB",
                    $"AnnounceEntry failed: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Process keyboard shortcuts for database daemons.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            bool ctrl = currentState.IsKeyDown(Keys.LeftControl)
                     || currentState.IsKeyDown(Keys.RightControl);
            bool focused = DisplayModulePatches.DisplayHasFocus;

            bool enter = (focused && Plugin.IsKeyPressed(Keys.Enter, currentState))
                      || (ctrl && Plugin.IsKeyPressed(Keys.Enter, currentState));
            bool up = (focused && Plugin.IsKeyPressed(Keys.Up, currentState))
                   || (ctrl && Plugin.IsKeyPressed(Keys.Up, currentState));
            bool down = (focused && Plugin.IsKeyPressed(Keys.Down, currentState))
                     || (ctrl && Plugin.IsKeyPressed(Keys.Down, currentState));
            bool escape = Plugin.IsKeyPressed(Keys.Escape, currentState);

            // Academic DB input
            if (_academicActive)
            {
                _academicActive = false;

                switch (_lastAcademicState)
                {
                    case 0: // Welcome
                        if (enter)
                            _pendingButton = 456001; // Search
                        else if (escape)
                            _pendingButton = 456005; // Exit
                        break;

                    case 5: // EntryNotFound
                    case 3: // Entry
                        if (enter)
                            _pendingButton = 456015; // Search Again
                        else if (escape)
                            _pendingButton = 456010; // Back
                        break;

                    case 6: // MultipleEntriesFound
                        if (up && _academicMatchCount > 0)
                        {
                            if (_academicMatchIndex > 0) _academicMatchIndex--;
                            AnnounceCurrentMatch();
                        }
                        else if (down && _academicMatchCount > 0)
                        {
                            if (_academicMatchIndex < _academicMatchCount - 1) _academicMatchIndex++;
                            AnnounceCurrentMatch();
                        }
                        else if (enter && _academicMatchCount > 0)
                        {
                            _pendingButton = 1237000 + _academicMatchIndex;
                        }
                        else if (escape)
                            _pendingButton = 12346085; // Go Back
                        break;
                }
            }

            // Clear medical active flag each frame
            _medicalActive = false;

            // Death Row DB input
            if (_deathRowActive)
            {
                _deathRowActive = false;

                if (_lastDeathRowIndex >= 0 && Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 98102855; // Return
                }
                else if (_lastDeathRowIndex < 0 && Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 166261601; // Exit
                }
            }
        }

        /// <summary>
        /// Reset all database daemon state.
        /// </summary>
        public static void Reset()
        {
            _lastAcademicState = -1;
            _academicActive = false;
            _academicMatchIndex = 0;
            _academicMatchCount = 0;
            _pendingButton = -1;
            _academicInstance = null;

            _lastMedicalState = -1;
            _medicalActive = false;

            _lastDeathRowIndex = -2;
            _deathRowActive = false;
            _deathRowInstance = null;
        }
    }
}
