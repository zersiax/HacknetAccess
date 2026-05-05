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

        // Generic DatabaseDaemon (CFC Records, etc.)
        private static int _lastGenericState = -1;
        private static bool _genericActive;
        private static object _genericInstance;
        private static int _genericRecordIndex;
        private static int _genericRecordCount;

        /// <summary>
        /// Whether any database daemon is currently being drawn.
        /// Used by DisplayModulePatches to detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _academicActive || _deathRowActive
            || _medicalActive || _genericActive;

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
                                    AnnounceCurrentMatch(false);
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
        private static void AnnounceCurrentMatch(bool interrupt = true)
        {
            if (_academicMatchIndex < 0 || _academicMatchIndex >= _academicMatchCount) return;
            Plugin.Announce(Loc.Get("db.matchItem",
                _academicMatchIndex + 1, _academicMatchCount,
                _academicMatchNames[_academicMatchIndex]), interrupt);
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
                                Plugin.Announce(Loc.Get("db.medicalMain"), false);
                                break;
                            case 1: // Search
                                Plugin.Announce(Loc.Get("db.medicalSearchPrompt"), false);
                                break;
                            case 3: // Entry
                                AnnounceMedicalEntry(__instance);
                                break;
                            case 4: // Error
                                Plugin.Announce(Loc.Get("db.notFound"), false);
                                break;
                            case 5: // AboutScreen
                                Plugin.Announce(Loc.Get("db.medicalInfo"), false);
                                break;
                            case 6: // SendReport
                                Plugin.Announce(Loc.Get("db.medicalSend"), false);
                                break;
                            case 7: // SendReportSearch
                                Plugin.Announce(Loc.Get("db.medicalSendPrompt"), false);
                                break;
                            case 9: // SendReportComplete
                                Plugin.Announce(Loc.Get("db.medicalSendDone"), false);
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation (except MainMenu where it exits)
                    if (_lastMedicalState != 0 && _lastMedicalState != -1)
                        DisplayModulePatches.DaemonClaimsEscape = true;
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

        #region Generic DatabaseDaemon (CFC Records, etc.)

        /// <summary>
        /// Prefix on DatabaseDaemon.draw — mark active.
        /// </summary>
        [HarmonyPatch]
        static class GenericDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _genericActive = true;
                _genericInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on DatabaseDaemon.draw — announce state changes and record list.
        /// State enum: Welcome(0), Search(1, unused), Browse(2), Loading(3),
        /// EntryDisplay(4), Error(5).
        /// </summary>
        [HarmonyPatch]
        static class GenericDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.DatabaseDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance)
            {
                try
                {
                    var type = __instance.GetType();
                    int stateVal = (int)AccessTools.Field(type, "State").GetValue(__instance);

                    if (stateVal != _lastGenericState)
                    {
                        _lastGenericState = stateVal;

                        switch (stateVal)
                        {
                            case 0: // Welcome
                                AnnounceGenericWelcome(__instance);
                                break;
                            case 2: // Browse
                                BuildGenericRecordList(__instance);
                                _genericRecordIndex = 0;
                                AnnounceGenericBrowse();
                                break;
                            case 4: // EntryDisplay
                                AnnounceGenericEntry(__instance);
                                break;
                            case 5: // Error
                                Plugin.Announce(Loc.Get("db.notFound"), false);
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation in sub-states
                    if (_lastGenericState == 2 || _lastGenericState == 4
                        || _lastGenericState == 5)
                        DisplayModulePatches.DaemonClaimsEscape = true;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "GenericDB",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Announce welcome screen — name + access state + available actions.
        /// </summary>
        private static void AnnounceGenericWelcome(object instance)
        {
            try
            {
                var type = instance.GetType();
                string name = (string)AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "name")
                    ?.GetValue(instance) ?? "Database";

                var permissionsField = AccessTools.Field(type, "Permissions");
                int permissions = (int)permissionsField.GetValue(instance);
                var comp = AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "comp")
                    .GetValue(instance);
                string adminIP = (string)AccessTools.Field(comp.GetType(), "adminIP")
                    .GetValue(comp);
                var os = AccessTools.Field(
                    AccessTools.TypeByName("Hacknet.Daemon"), "os")
                    .GetValue(instance);
                var thisComp = AccessTools.Field(os.GetType(), "thisComputer")
                    .GetValue(os);
                string thisIP = (string)AccessTools.Field(thisComp.GetType(), "ip")
                    .GetValue(thisComp);

                // Permissions: 0 = AdminOnly, 1 = Public
                bool hasAccess = permissions == 1 || adminIP == thisIP;
                string key = hasAccess
                    ? "db.genericWelcome"
                    : "db.genericWelcomeNoAccess";
                Plugin.Announce(Loc.Get(key, name), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "GenericDB",
                    $"AnnounceWelcome failed: {ex.Message}");
                Plugin.Announce(Loc.Get("db.genericWelcome", "Database"), false);
            }
        }

        /// <summary>
        /// Build the list of record names for navigation.
        /// </summary>
        private static void BuildGenericRecordList(object instance)
        {
            _genericRecordCount = 0;
            try
            {
                var type = instance.GetType();
                var folder = AccessTools.Field(type, "DatasetFolder").GetValue(instance);
                if (folder == null) return;
                var files = AccessTools.Field(folder.GetType(), "files")
                    ?.GetValue(folder) as IList;
                if (files == null) return;
                _genericRecordCount = files.Count;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "GenericDB",
                    $"BuildRecordList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the display name for a record at the given index, formatted via
        /// the daemon's GetAnnounceNameForFileName method.
        /// </summary>
        private static string GetGenericRecordName(int index)
        {
            try
            {
                if (_genericInstance == null) return null;
                var type = _genericInstance.GetType();
                var folder = AccessTools.Field(type, "DatasetFolder").GetValue(_genericInstance);
                if (folder == null) return null;
                var files = AccessTools.Field(folder.GetType(), "files")
                    ?.GetValue(folder) as IList;
                if (files == null || index < 0 || index >= files.Count) return null;

                var file = files[index];
                string filename = (string)AccessTools.Field(file.GetType(), "name")
                    ?.GetValue(file) ?? "";

                // Mirror DatabaseDaemon.GetAnnounceNameForFileName
                filename = filename.Replace(".rec", "");
                bool filenameIsPersonName = (bool)AccessTools.Field(type, "FilenameIsPersonName")
                    .GetValue(_genericInstance);
                if (filenameIsPersonName)
                {
                    string[] parts = filename.Split('_');
                    if (parts.Length >= 2)
                        return parts[1] + " " + parts[0];
                }
                return filename;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "GenericDB",
                    $"GetRecordName failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Announce arrival at the Browse screen with the first record.
        /// </summary>
        private static void AnnounceGenericBrowse()
        {
            if (_genericRecordCount == 0)
            {
                Plugin.Announce(Loc.Get("db.genericBrowseEmpty"), false);
                return;
            }
            Plugin.Announce(Loc.Get("db.genericBrowse", _genericRecordCount), false);
            AnnounceGenericRecord(false);
        }

        /// <summary>
        /// Announce the currently selected record.
        /// </summary>
        private static void AnnounceGenericRecord(bool interrupt = true)
        {
            if (_genericRecordCount == 0) return;
            string name = GetGenericRecordName(_genericRecordIndex) ?? "Unknown";
            Plugin.Announce(Loc.Get("db.genericRecord",
                _genericRecordIndex + 1, _genericRecordCount, name), interrupt);
        }

        /// <summary>
        /// Announce the contents of the currently displayed entry.
        /// Reads ActiveFile.data and strips XML markers.
        /// </summary>
        private static void AnnounceGenericEntry(object instance)
        {
            try
            {
                var type = instance.GetType();
                var activeFile = AccessTools.Field(type, "ActiveFile")?.GetValue(instance);
                if (activeFile == null)
                {
                    Plugin.Announce(Loc.Get("db.entry", "Unknown"), false);
                    return;
                }

                string filename = (string)AccessTools.Field(activeFile.GetType(), "name")
                    ?.GetValue(activeFile) ?? "";
                string data = (string)AccessTools.Field(activeFile.GetType(), "data")
                    ?.GetValue(activeFile) ?? "";

                // Mirror name formatting
                string displayName = filename.Replace(".rec", "");
                bool filenameIsPersonName = (bool)AccessTools.Field(type, "FilenameIsPersonName")
                    .GetValue(instance);
                if (filenameIsPersonName)
                {
                    string[] parts = displayName.Split('_');
                    if (parts.Length >= 2)
                        displayName = parts[1] + " " + parts[0];
                }

                // Clean XML markers (data uses [ ] instead of < >)
                string cleaned = data.Replace("[", "").Replace("]", " ");
                // Collapse whitespace
                cleaned = System.Text.RegularExpressions.Regex.Replace(
                    cleaned, @"\s+", " ").Trim();
                if (cleaned.Length > 800)
                    cleaned = cleaned.Substring(0, 800) + "...";

                Plugin.Announce(Loc.Get("db.entry", displayName) + ". " + cleaned, false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "GenericDB",
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

            // Medical DB input
            if (_medicalActive)
            {
                _medicalActive = false;

                bool rKey = focused && Plugin.IsKeyPressed(Keys.R, currentState);
                bool iKey = focused && Plugin.IsKeyPressed(Keys.I, currentState);

                switch (_lastMedicalState)
                {
                    case 0: // MainMenu
                        if (enter)
                            _pendingButton = 444402005; // Search
                        else if (rKey)
                            _pendingButton = 444402007; // Random Entry
                        else if (iKey)
                            _pendingButton = 444402000; // Info / About
                        else if (escape)
                            _pendingButton = 444402800; // Exit Database View
                        break;

                    case 3: // Entry
                        if (enter)
                            _pendingButton = 444402035; // e-mail this record
                        else if (escape)
                            _pendingButton = 444402033; // Back to menu
                        break;

                    case 4: // Error
                        if (enter || escape)
                            _pendingButton = 444402002; // Back to menu
                        break;

                    case 5: // AboutScreen
                        if (enter || escape)
                            _pendingButton = 444402002; // Back to menu
                        break;

                    case 6: // SendReport
                        if (enter)
                            _pendingButton = 444402023; // Specify Address
                        else if (escape)
                            _pendingButton = 444402002; // Back to menu
                        break;

                    case 9: // SendReportComplete
                        if (enter)
                            _pendingButton = 444402001; // Send to different address
                        else if (escape)
                            _pendingButton = 444402002; // Back to menu
                        break;
                }
            }

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

            // Generic DB input (CFC Records, etc.)
            if (_genericActive)
            {
                _genericActive = false;

                bool lKey = focused && Plugin.IsKeyPressed(Keys.L, currentState);

                switch (_lastGenericState)
                {
                    case 0: // Welcome
                        if (enter)
                            _pendingButton = 73616101; // Browse Records
                        else if (lKey)
                            _pendingButton = 73616102; // Login
                        else if (escape)
                            _pendingButton = 73616129; // Exit
                        break;

                    case 2: // Browse
                        if (up && _genericRecordCount > 0)
                        {
                            if (_genericRecordIndex > 0) _genericRecordIndex--;
                            AnnounceGenericRecord();
                        }
                        else if (down && _genericRecordCount > 0)
                        {
                            if (_genericRecordIndex < _genericRecordCount - 1)
                                _genericRecordIndex++;
                            AnnounceGenericRecord();
                        }
                        else if (enter && _genericRecordCount > 0)
                        {
                            _pendingButton = 71118100 + _genericRecordIndex;
                        }
                        else if (escape)
                            _pendingButton = 71118000; // Back
                        break;

                    case 4: // EntryDisplay
                        if (escape)
                            _pendingButton = 7301991; // Back
                        break;

                    case 5: // Error
                        if (enter || escape)
                            _pendingButton = 73616101; // Back
                        break;
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

            _lastGenericState = -1;
            _genericActive = false;
            _genericInstance = null;
            _genericRecordIndex = 0;
            _genericRecordCount = 0;
        }
    }
}
