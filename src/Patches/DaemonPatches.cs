using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for simple daemons that don't need keyboard navigation:
    /// IRCDaemon (chat log) and WebServerDaemon (page display).
    /// MailServer and DatabaseDaemon are handled by dedicated patch files.
    /// MessageBoardDaemon is handled by MessageBoardPatches.
    /// </summary>
    [HarmonyPatch]
    internal static class DaemonPatches
    {
        // IRC: track log data length to detect new messages
        private static int _lastIrcLogLength;
        private static string _lastIrcLogData;
        private static int _ircDrawFrame;

        // Web: track last announced URL and content
        private static string _lastWebUrl;
        private static string _lastWebContent;
        private static int _webDrawFrame;

        /// <summary>
        /// Whether a simple daemon (IRC or Web) is currently drawing.
        /// Uses frame-counter pattern (like MailPatches) so the flag
        /// survives the Update→Draw phase gap.
        /// </summary>
        public static bool IsActive =>
            (AccessStateManager.FrameCount - _ircDrawFrame) <= 2
            || (AccessStateManager.FrameCount - _webDrawFrame) <= 2;

        /// <summary>
        /// Get the last extracted web page text for F5 re-read.
        /// </summary>
        public static string LastWebContent => _lastWebContent;

        /// <summary>
        /// Reset all cached state. Called on disconnect.
        /// </summary>
        public static void Reset()
        {
            _lastIrcLogLength = 0;
            _lastIrcLogData = null;
            _ircDrawFrame = 0;
            _lastWebUrl = null;
            _lastWebContent = null;
            _webDrawFrame = 0;
        }

        /// <summary>
        /// After IRCDaemon.draw, announce new chat messages.
        /// IRC stores messages in System.ActiveLogFile.data as entries
        /// delimited by "\n#". Each entry: "timestamp//author//message".
        /// </summary>
        [HarmonyPatch]
        static class IrcDrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.IRCDaemon"),
                    "draw");
            }

            static void Postfix(object __instance)
            {
                _ircDrawFrame = AccessStateManager.FrameCount;
                try
                {
                    var type = __instance.GetType();
                    var system = AccessTools.Field(type, "System")?.GetValue(__instance);
                    if (system == null) return;

                    var logFile = AccessTools.Field(system.GetType(), "ActiveLogFile")
                        ?.GetValue(system);
                    if (logFile == null) return;

                    string data = (string)AccessTools.Field(logFile.GetType(), "data")
                        ?.GetValue(logFile);
                    if (data == null) return;

                    // Detect new messages by data length change
                    if (data.Length == _lastIrcLogLength) return;

                    bool isFirstLoad = _lastIrcLogData == null;
                    string previousData = _lastIrcLogData ?? "";
                    _lastIrcLogLength = data.Length;
                    _lastIrcLogData = data;

                    if (isFirstLoad)
                    {
                        // First time seeing this IRC — announce last few messages
                        AnnounceRecentIrcMessages(data, 3);
                        return;
                    }

                    // Find new content (data grows by appending "\n#entry")
                    if (data.Length > previousData.Length
                        && data.StartsWith(previousData.Substring(0,
                            Math.Min(previousData.Length, 50))))
                    {
                        string newPart = data.Substring(previousData.Length);
                        AnnounceIrcEntries(newPart);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "IRC",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Announce the last N messages from IRC log data.
        /// </summary>
        private static void AnnounceRecentIrcMessages(string data, int count)
        {
            try
            {
                string[] entries = data.Split(
                    new[] { "\n#" }, StringSplitOptions.RemoveEmptyEntries);

                int start = Math.Max(0, entries.Length - count);
                var sb = new StringBuilder();
                for (int i = start; i < entries.Length; i++)
                {
                    string formatted = FormatIrcEntry(entries[i]);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(formatted);
                    }
                }

                if (sb.Length > 0)
                {
                    Plugin.Announce(Loc.Get("daemon.ircMessage", sb.ToString()), false);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "IRC",
                    $"AnnounceRecentIrcMessages failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce new IRC entries from a raw data fragment.
        /// </summary>
        private static void AnnounceIrcEntries(string fragment)
        {
            try
            {
                string[] entries = fragment.Split(
                    new[] { "\n#" }, StringSplitOptions.RemoveEmptyEntries);

                var sb = new StringBuilder();
                foreach (string entry in entries)
                {
                    string formatted = FormatIrcEntry(entry);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(formatted);
                    }
                }

                if (sb.Length > 0)
                {
                    Plugin.Announce(Loc.Get("daemon.ircMessage", sb.ToString()), false);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "IRC",
                    $"AnnounceIrcEntries failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract readable text from a WebServerDaemon's loaded HTML page.
        /// Reads lastLoadedFile.data, strips HTML tags, collapses whitespace.
        /// </summary>
        private static string ExtractWebText(object webDaemon)
        {
            try
            {
                var type = webDaemon.GetType();
                var fileEntry = AccessTools.Field(type, "lastLoadedFile")
                    ?.GetValue(webDaemon);
                if (fileEntry == null) return null;

                string html = (string)AccessTools.Field(fileEntry.GetType(), "data")
                    ?.GetValue(fileEntry);
                if (string.IsNullOrEmpty(html)) return null;

                // Strip script and style blocks entirely
                string text = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>",
                    "", RegexOptions.IgnoreCase);
                // Replace <br>, <p>, <div>, <li>, <tr>, <h1>-<h6> with newlines
                text = Regex.Replace(text, @"<(br|/p|/div|/li|/tr|/h[1-6])[^>]*>",
                    "\n", RegexOptions.IgnoreCase);
                // Strip remaining tags
                text = Regex.Replace(text, @"<[^>]+>", " ");
                // Decode common HTML entities
                text = text.Replace("&amp;", "&").Replace("&lt;", "<")
                    .Replace("&gt;", ">").Replace("&quot;", "\"")
                    .Replace("&#39;", "'").Replace("&nbsp;", " ");
                // Collapse whitespace within lines, preserve line breaks
                var lines = text.Split(new[] { '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                foreach (string line in lines)
                {
                    string trimmed = Regex.Replace(line, @"\s+", " ").Trim();
                    if (trimmed.Length > 0)
                    {
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append(trimmed);
                    }
                }
                string result = sb.ToString();
                return result.Length > 0 ? result : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "WebServer",
                    $"ExtractWebText failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Format a single IRC log entry ("timestamp//author//message") for announcement.
        /// </summary>
        private static string FormatIrcEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return null;
            entry = entry.TrimStart('#');

            string[] parts = entry.Split(new[] { "//" }, 3, StringSplitOptions.None);
            if (parts.Length < 3) return entry.Trim();

            string author = parts[1];
            string message = parts[2].Replace("&dsr", "//");

            // Skip attachment/announcement markers for simple announcement
            if (message.StartsWith("!ATTACHMENT:"))
                return $"{author}: [attachment]";
            if (message.StartsWith("!ANNOUNCEMENT!"))
                message = message.Substring("!ANNOUNCEMENT!".Length);

            return $"{author}: {message.Trim()}";
        }

        /// <summary>
        /// After WebServerDaemon.draw, announce URL changes.
        /// </summary>
        [HarmonyPatch]
        static class WebServerDrawPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.WebServerDaemon"),
                    "draw");
            }

            static void Postfix(object __instance)
            {
                _webDrawFrame = AccessStateManager.FrameCount;
                try
                {
                    var type = __instance.GetType();
                    string url = (string)AccessTools.Field(type, "saveURL")
                        ?.GetValue(__instance);
                    if (url == null) return;

                    // Extract text from lastLoadedFile.data (HTML content)
                    string pageText = ExtractWebText(__instance);
                    _lastWebContent = pageText;

                    if (url == _lastWebUrl) return;
                    _lastWebUrl = url;

                    if (!string.IsNullOrEmpty(pageText))
                    {
                        Plugin.Announce(Loc.Get("daemon.webPage", pageText), false);
                    }
                    else
                    {
                        // Fallback: announce server name + URL
                        string serverName = (string)AccessTools.Field(
                            AccessTools.TypeByName("Hacknet.Daemon"), "name")
                            ?.GetValue(__instance) ?? "Web Server";
                        Plugin.Announce(Loc.Get("daemon.webPage",
                            serverName + "/" + url), false);
                    }
                    DebugLogger.Log(LogCategory.Handler, "WebServer",
                        $"Page: {url}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "WebServer",
                        $"DrawPostfix failed: {ex.Message}");
                }
            }
        }
    }
}
