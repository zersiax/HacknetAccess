using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

        // Web: track last announced URL
        private static string _lastWebUrl;

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
                try
                {
                    var type = __instance.GetType();
                    string url = (string)AccessTools.Field(type, "saveURL")
                        ?.GetValue(__instance);
                    if (url == null || url == _lastWebUrl) return;
                    _lastWebUrl = url;

                    // Get the server name from the daemon
                    string serverName = (string)AccessTools.Field(
                        AccessTools.TypeByName("Hacknet.Daemon"), "name")
                        ?.GetValue(__instance) ?? "Web Server";

                    Plugin.Announce(Loc.Get("daemon.webPage",
                        serverName + "/" + url), false);
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
