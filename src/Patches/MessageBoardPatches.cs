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
    /// Patches for MessageBoardDaemon — 4chan-style image boards.
    /// States: Board (list of thread previews), Thread (full thread view).
    /// Up/Down navigates threads, Enter views thread, Escape goes back.
    /// Items are called "threads"; each thread has "posts".
    /// </summary>
    [HarmonyPatch]
    internal static class MessageBoardPatches
    {
        // 0 = Thread (viewing a thread), 1 = Board (listing threads)
        private static int _lastState = -1;
        private static int _threadIndex;
        private static int _threadCount;
        private static bool _boardActive;
        private static int _pendingButton = -1;
        private static List<string> _threadPreviews = new List<string>();
        private static List<int> _threadButtonIds = new List<int>();
        private static object _boardInstance;
        private static string _boardName;

        /// <summary>
        /// Whether the message board daemon is currently being drawn.
        /// Used by DisplayModulePatches to detect interactive daemon presence.
        /// </summary>
        public static bool IsActive => _boardActive;

        /// <summary>
        /// Prefix on MessageBoardDaemon.draw — mark as active.
        /// </summary>
        [HarmonyPatch]
        static class BoardDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MessageBoardDaemon"),
                    "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix(object __instance)
            {
                _boardActive = true;
                _boardInstance = __instance;
            }
        }

        /// <summary>
        /// Postfix on MessageBoardDaemon.draw — detect state changes.
        /// </summary>
        [HarmonyPatch]
        static class BoardDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MessageBoardDaemon"),
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

                    if (stateVal != _lastState)
                    {
                        int prevState = _lastState;
                        _lastState = stateVal;

                        // Read board name
                        if (_boardName == null)
                        {
                            _boardName = (string)AccessTools.Field(type, "BoardName")
                                ?.GetValue(__instance) ?? "Message Board";
                        }

                        switch (stateVal)
                        {
                            case 1: // Board
                                BuildThreadList(__instance);
                                if (prevState == 0)
                                {
                                    // Returning from thread view — keep index
                                    AnnounceCurrentThread();
                                }
                                else
                                {
                                    _threadIndex = 0;
                                    Plugin.Announce(Loc.Get("board.title",
                                        _boardName, _threadCount), false);
                                    if (_threadCount > 0)
                                        AnnounceCurrentThread();
                                }
                                break;

                            case 0: // Thread (viewing full thread)
                                AnnounceThreadContent(__instance);
                                break;
                        }
                    }

                    // Claim Escape for internal back-navigation in thread view
                    if (_lastState == 0)
                        DisplayModulePatches.DaemonClaimsEscape = true;

                    // Clear stale pending button
                    if (_pendingButton != -1)
                    {
                        DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                            $"Stale pending button {_pendingButton} cleared");
                        _pendingButton = -1;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch Button.doButton for message board button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class BoardButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                        $"Activated button: {myID}");
                }
            }
        }

        /// <summary>
        /// Build the list of thread previews from threadsFolder.files.
        /// Parses each thread file to extract ID (used for button hash)
        /// and first post text (used for preview).
        /// </summary>
        private static void BuildThreadList(object board)
        {
            _threadPreviews.Clear();
            _threadButtonIds.Clear();
            _threadCount = 0;

            try
            {
                var type = board.GetType();
                var threadsFolder = AccessTools.Field(type, "threadsFolder")
                    ?.GetValue(board);
                if (threadsFolder == null) return;

                var files = AccessTools.Field(threadsFolder.GetType(), "files")
                    ?.GetValue(threadsFolder) as IList;
                if (files == null) return;

                string[] separator = { "------------------------------------------\r\n",
                                       "------------------------------------------\n",
                                       "------------------------------------------" };

                foreach (var file in files)
                {
                    if (file == null) continue;

                    string data = (string)AccessTools.Field(file.GetType(), "data")
                        ?.GetValue(file);
                    if (string.IsNullOrEmpty(data)) continue;

                    // Parse: first line is thread ID, then posts separated by separator
                    string[] parts = data.Split(separator, StringSplitOptions.None);
                    string threadId = parts[0].Trim();

                    // First post text (preview)
                    string preview = "";
                    if (parts.Length > 1)
                    {
                        preview = parts[1].Trim();
                        // Strip image markers (#ImageName\n)
                        if (preview.StartsWith("#"))
                        {
                            int nlIdx = preview.IndexOf('\n');
                            if (nlIdx >= 0)
                                preview = preview.Substring(nlIdx + 1).Trim();
                        }
                        // Truncate for announcement
                        if (preview.Length > 200)
                            preview = preview.Substring(0, 200) + "...";
                    }

                    // Button ID mirrors game: 17839000 + threadId.GetHashCode()
                    int buttonId = 17839000 + threadId.GetHashCode();

                    _threadPreviews.Add(preview);
                    _threadButtonIds.Add(buttonId);
                    _threadCount++;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                    $"BuildThreadList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the currently selected thread preview.
        /// </summary>
        private static void AnnounceCurrentThread()
        {
            if (_threadIndex < 0 || _threadIndex >= _threadCount) return;
            Plugin.Announce(Loc.Get("board.thread",
                _threadIndex + 1, _threadCount, _threadPreviews[_threadIndex]));
        }

        /// <summary>
        /// Announce thread content when viewing a full thread.
        /// Reads viewingThread's posts.
        /// </summary>
        private static void AnnounceThreadContent(object board)
        {
            try
            {
                var type = board.GetType();
                var viewingThread = AccessTools.Field(type, "viewingThread")
                    ?.GetValue(board);
                if (viewingThread == null) return;

                var posts = AccessTools.Field(viewingThread.GetType(), "posts")
                    ?.GetValue(viewingThread) as IList;
                if (posts == null || posts.Count == 0) return;

                var sb = new StringBuilder();
                for (int i = 0; i < posts.Count; i++)
                {
                    var post = posts[i];
                    // MessageBoardPost is a struct with fields: text, img
                    string text = (string)AccessTools.Field(post.GetType(), "text")
                        ?.GetValue(post) ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append(text.Trim());
                    }
                }

                // Truncate very long threads
                string content = sb.ToString();
                if (content.Length > 1000)
                    content = content.Substring(0, 1000) + "...";

                Plugin.Announce(Loc.Get("board.threadView", content)
                    + " " + Loc.Get("board.back"), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                    $"AnnounceThreadContent failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process keyboard shortcuts when board is active.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!_boardActive) return;
            _boardActive = false;

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

            switch (_lastState)
            {
                case 1: // Board
                    if (up && _threadCount > 0)
                    {
                        if (_threadIndex > 0) _threadIndex--;
                        AnnounceCurrentThread();
                    }
                    else if (down && _threadCount > 0)
                    {
                        if (_threadIndex < _threadCount - 1) _threadIndex++;
                        AnnounceCurrentThread();
                    }
                    else if (enter && _threadCount > 0
                        && _threadIndex < _threadButtonIds.Count)
                    {
                        _pendingButton = _threadButtonIds[_threadIndex];
                        DebugLogger.Log(LogCategory.Handler, "MessageBoard",
                            $"View thread pending: button {_pendingButton}");
                    }
                    break;

                case 0: // Thread (viewing)
                    if (escape)
                    {
                        _pendingButton = 1931655802; // Back to Board
                    }
                    break;
            }
        }

        /// <summary>
        /// Reset state when leaving context.
        /// </summary>
        public static void Reset()
        {
            _lastState = -1;
            _threadIndex = 0;
            _threadCount = 0;
            _boardActive = false;
            _pendingButton = -1;
            _boardInstance = null;
            _boardName = null;
        }
    }
}
