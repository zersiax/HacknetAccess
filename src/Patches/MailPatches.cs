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
    /// Patches for mail accessibility.
    /// F6 opens the mail inbox.
    /// Ctrl+Up/Down navigates inbox or email lines.
    /// Ctrl+Enter opens email / sends reply.
    /// Ctrl+R replies to email.
    /// Escape goes back.
    /// </summary>
    [HarmonyPatch]
    internal static class MailPatches
    {
        private static int _lastState = -1;
        private static int _emailIndex;
        private static int _emailCount;
        private static List<string> _inboxSenders = new List<string>();
        private static List<string> _inboxSubjects = new List<string>();
        private static List<bool> _inboxUnread = new List<bool>();
        private static int _pendingButton = -1;
        private static bool _mailActive;
        private static int _mailDrawFrame;

        /// <summary>Whether mail daemon is currently being drawn (within last 2 frames).</summary>
        public static bool IsActive => _mailActive
            && (AccessStateManager.FrameCount - _mailDrawFrame) <= 2;

        /// <summary>Whether terminal input should be suppressed for mail.
        /// Excludes reply state (5) where the user needs to type into getString.</summary>
        public static bool HasFocus => IsActive && _lastState != 5;

        // Email viewer line navigation
        private static string[] _emailLines;
        private static int _emailLineIndex;

        // Attachment tracking
        private static List<int> _attachmentButtonIds = new List<int>();
        private static List<string> _attachmentDescriptions = new List<string>();
        private static int _attachmentIndex;
        private static Dictionary<int, int> _lineToAttachmentMap = new Dictionary<int, int>();

        /// <summary>
        /// Prefix on MailServer.draw — mark mail as active.
        /// </summary>
        [HarmonyPatch]
        static class MailServerDrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MailServer"), "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Prefix()
            {
                _mailActive = true;
                _mailDrawFrame = AccessStateManager.FrameCount;
            }
        }

        /// <summary>
        /// Postfix on MailServer.draw — detect state changes and announce content.
        /// </summary>
        [HarmonyPatch]
        static class MailServerDrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MailServer"), "draw",
                    new[] { typeof(Microsoft.Xna.Framework.Rectangle),
                            typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) });
            }

            static void Postfix(object __instance, int ___state, string[] ___emailData)
            {
                if (___state != _lastState)
                {
                    int prevState = _lastState;
                    _lastState = ___state;

                    switch (___state)
                    {
                        case 0:
                            Plugin.Announce(Loc.Get("mail.server"), false);
                            break;
                        case 3:
                            BuildInboxList(__instance);
                            if (_emailCount > 0)
                            {
                                if (prevState == 4 || prevState == 5)
                                {
                                    AnnounceCurrentEmail();
                                }
                                else
                                {
                                    _emailIndex = 0;
                                    Plugin.Announce(Loc.Get("mail.inbox", _emailCount), false);
                                    AnnounceCurrentEmail();
                                }
                            }
                            else
                            {
                                Plugin.Announce(Loc.Get("mail.empty"), false);
                            }
                            break;
                        case 4:
                            AnnounceEmailContent(___emailData);
                            break;
                        case 5:
                            Plugin.Announce(Loc.Get("mail.reply"), false);
                            break;
                    }
                }

                // Claim Escape for internal back-navigation in sub-states
                if (___state == 0 || ___state == 3 || ___state == 4 || ___state == 5)
                    DisplayModulePatches.DaemonClaimsEscape = true;
            }
        }

        /// <summary>
        /// Patch Button.doButton to handle mail button activation.
        /// </summary>
        [HarmonyPatch(typeof(Hacknet.Gui.Button), nameof(Hacknet.Gui.Button.doButton),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(string), typeof(Microsoft.Xna.Framework.Color?))]
        static class MailButtonPatch
        {
            static void Prefix(int myID)
            {
                if (_pendingButton != -1 && _pendingButton == myID)
                {
                    _pendingButton = -1;
                    Hacknet.GuiData.hot = myID;
                    Hacknet.GuiData.active = myID;
                    DebugLogger.Log(LogCategory.Handler, "Mail",
                        $"Activated button: {myID}");
                }
            }
        }


        /// <summary>
        /// Postfix on MailIcon.mailReceived — announce new mail.
        /// </summary>
        [HarmonyPatch]
        static class MailReceivedPatch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.MailIcon"), "mailReceived",
                    new[] { typeof(string), typeof(string) });
            }

            static void Postfix()
            {
                Plugin.Announce(Loc.Get("notify.mail"), false);
            }
        }

        /// <summary>
        /// Build the inbox email list from the mail server's user folder.
        /// </summary>
        private static void BuildInboxList(object mailServer)
        {
            _inboxSenders.Clear();
            _inboxSubjects.Clear();
            _inboxUnread.Clear();
            _emailCount = 0;

            try
            {
                var userFolder = AccessTools.Field(mailServer.GetType(), "userFolder")
                    ?.GetValue(mailServer);
                if (userFolder == null) return;

                var folders = AccessTools.Field(userFolder.GetType(), "folders")
                    ?.GetValue(userFolder) as IList;
                if (folders == null || folders.Count == 0) return;

                var inbox = folders[0];
                var files = AccessTools.Field(inbox.GetType(), "files")
                    ?.GetValue(inbox) as IList;
                if (files == null) return;

                string delimiter = (string)AccessTools.Field(mailServer.GetType(), "emailSplitDelimiter")
                    ?.GetValue(null);
                if (delimiter == null) delimiter = "@*&^#%@)_!_)*#^@!&*)(#^&\n";
                string[] delims = new[] { delimiter };

                foreach (var file in files)
                {
                    string data = AccessTools.Field(file.GetType(), "data")
                        ?.GetValue(file) as string;
                    if (string.IsNullOrEmpty(data)) continue;

                    string[] parts = data.Split(delims, StringSplitOptions.None);
                    if (parts.Length < 3) continue;

                    bool unread = parts[0] == "0";
                    _inboxUnread.Add(unread);
                    _inboxSenders.Add(parts[1]);
                    _inboxSubjects.Add(parts[2]);
                    _emailCount++;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Mail",
                    $"BuildInboxList failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce the currently focused email in the inbox.
        /// </summary>
        private static void AnnounceCurrentEmail()
        {
            if (_emailIndex < 0 || _emailIndex >= _emailCount) return;

            string unread = _inboxUnread[_emailIndex] ? Loc.Get("mail.unread") : "";
            string sender = _inboxSenders[_emailIndex];
            string subject = _inboxSubjects[_emailIndex];

            Plugin.Announce(Loc.Get("mail.item", _emailIndex + 1, _emailCount,
                unread, sender, subject));
        }

        /// <summary>
        /// Announce email content and build line array for navigation.
        /// Always announces (no dedup) since user may re-open the same email.
        /// </summary>
        private static void AnnounceEmailContent(string[] emailData)
        {
            if (emailData == null || emailData.Length < 4) return;

            try
            {
                string sender = emailData[1];
                string subject = emailData[2];
                string body = emailData[3];

                // Build lines for Ctrl+Up/Down navigation
                var lines = new List<string>();
                lines.Add(Loc.Get("daemon.mailFrom", sender));
                lines.Add(Loc.Get("daemon.mailSubject", subject));

                // Split body into lines
                string[] bodyLines = body.Split(new[] { '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in bodyLines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0)
                        lines.Add(trimmed);
                }

                // Parse attachments
                _attachmentButtonIds.Clear();
                _attachmentDescriptions.Clear();
                _lineToAttachmentMap.Clear();
                _attachmentIndex = 0;
                string[] spaceDelim = new[] { "#%#" };
                int buttonIndex = 0;

                for (int i = 4; i < emailData.Length; i++)
                {
                    string[] parts = emailData[i].Split(spaceDelim, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1) continue;

                    string desc;
                    int buttonId;

                    if (parts[0] == "link" && parts.Length >= 3)
                    {
                        desc = $"Link: {parts[1]} at {parts[2]}";
                        buttonId = 800009 + buttonIndex;
                    }
                    else if (parts[0] == "account" && parts.Length >= 5)
                    {
                        desc = $"Account: {parts[1]} at {parts[2]}, User={parts[3]}, Pass={parts[4]}";
                        buttonId = 801009 + buttonIndex;
                    }
                    else if (parts[0] == "note" && parts.Length >= 2)
                    {
                        desc = $"Note: {parts[1]}";
                        buttonId = 800009 + buttonIndex;
                    }
                    else
                    {
                        continue;
                    }

                    _attachmentButtonIds.Add(buttonId);
                    _attachmentDescriptions.Add(desc);
                    _lineToAttachmentMap[lines.Count] = buttonIndex;
                    lines.Add(Loc.Get("mail.attachment", buttonIndex + 1, desc));
                    buttonIndex++;
                }

                _emailLines = lines.ToArray();
                _emailLineIndex = 0;

                // Announce full content
                var sb = new StringBuilder();
                foreach (string line in _emailLines)
                {
                    sb.Append(line);
                    sb.Append(" ");
                }
                Plugin.Announce(sb.ToString().Trim(), false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "Mail",
                    $"AnnounceEmailContent failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process mail keyboard shortcuts.
        /// Called from Plugin.ProcessInput() in the game update loop.
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            // F6 — open mail
            if (Plugin.IsKeyPressed(Keys.F6, currentState))
            {
                DebugLogger.LogInput("F6", "Open mail");

                try
                {
                    var osType = AccessTools.TypeByName("Hacknet.OS");
                    var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                    if (os == null) return;

                    var mailIcon = AccessTools.Field(osType, "mailicon")?.GetValue(os);
                    if (mailIcon == null)
                    {
                        Plugin.Announce(Loc.Get("mail.unavailable"));
                        return;
                    }

                    var connectMethod = AccessTools.Method(mailIcon.GetType(), "connectToMail");
                    connectMethod?.Invoke(mailIcon, null);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(LogCategory.Handler, "Mail",
                        $"F6 connectToMail failed: {ex.Message}");
                    Plugin.Announce(Loc.Get("mail.unavailable"));
                }
                return;
            }

            // Only process mail navigation when mail is actively displayed
            if (!IsActive) return;

            bool ctrl = currentState.IsKeyDown(Keys.LeftControl) || currentState.IsKeyDown(Keys.RightControl);

            if (_lastState == 0)
            {
                // Login screen — Enter to login, Escape to exit
                if (Plugin.IsKeyPressed(Keys.Enter, currentState))
                {
                    _pendingButton = 800002;
                }
                else if (Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 800003;
                }
            }
            else if (_lastState == 3)
            {
                // Inbox — Up/Down navigate, Enter opens, Escape logs out
                if (Plugin.IsKeyPressed(Keys.Up, currentState) && _emailCount > 0)
                {
                    if (_emailIndex > 0) _emailIndex--;
                    AnnounceCurrentEmail();
                }
                else if (Plugin.IsKeyPressed(Keys.Down, currentState) && _emailCount > 0)
                {
                    if (_emailIndex < _emailCount - 1) _emailIndex++;
                    AnnounceCurrentEmail();
                }
                else if (Plugin.IsKeyPressed(Keys.Enter, currentState) && _emailCount > 0)
                {
                    _pendingButton = 8100 + _emailIndex;
                }
                else if (Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 800007;
                }
            }
            else if (_lastState == 4)
            {
                // Email viewer — Up/Down navigate lines, Enter activates attachment,
                // Ctrl+R reply, Escape back
                if (Plugin.IsKeyPressed(Keys.Up, currentState)
                    && _emailLines != null && _emailLines.Length > 0)
                {
                    if (_emailLineIndex > 0) _emailLineIndex--;
                    // Sync attachment index when on an attachment line
                    if (_lineToAttachmentMap.TryGetValue(_emailLineIndex, out int upAttIdx))
                        _attachmentIndex = upAttIdx;
                    Plugin.Announce($"{_emailLines[_emailLineIndex]}. {_emailLineIndex + 1} of {_emailLines.Length}");
                }
                else if (Plugin.IsKeyPressed(Keys.Down, currentState)
                    && _emailLines != null && _emailLines.Length > 0)
                {
                    if (_emailLineIndex < _emailLines.Length - 1) _emailLineIndex++;
                    // Sync attachment index when on an attachment line
                    if (_lineToAttachmentMap.TryGetValue(_emailLineIndex, out int downAttIdx))
                        _attachmentIndex = downAttIdx;
                    Plugin.Announce($"{_emailLines[_emailLineIndex]}. {_emailLineIndex + 1} of {_emailLines.Length}");
                }
                else if (Plugin.IsKeyPressed(Keys.Enter, currentState)
                    && _lineToAttachmentMap.ContainsKey(_emailLineIndex)
                    && _attachmentButtonIds.Count > 0
                    && _attachmentIndex >= 0 && _attachmentIndex < _attachmentButtonIds.Count)
                {
                    // Activate attachment at current line
                    _pendingButton = _attachmentButtonIds[_attachmentIndex];
                    Plugin.Announce(Loc.Get("mail.attachmentActivated",
                        _attachmentDescriptions[_attachmentIndex]), false);
                }
                else if (ctrl && Plugin.IsKeyPressed(Keys.R, currentState))
                {
                    _pendingButton = 90200;
                }
                else if (Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 800007;
                }
            }
            else if (_lastState == 5)
            {
                // Reply screen — Ctrl+D to add detail, Ctrl+Enter to send, Escape to go back
                if (ctrl && Plugin.IsKeyPressed(Keys.D, currentState))
                {
                    // Only trigger "+" button if not already in getString mode
                    if (!TerminalPatches.IsTerminalInputMode())
                    {
                        _pendingButton = 8000098; // "+" add detail button
                        DebugLogger.Log(LogCategory.Handler, "Mail",
                            "Ctrl+D: adding reply detail");
                    }
                }
                else if (Plugin.IsKeyPressed(Keys.Enter, currentState)
                    && !TerminalPatches.IsTerminalInputMode())
                {
                    _pendingButton = 800008; // Send
                }
                else if (Plugin.IsKeyPressed(Keys.Escape, currentState))
                {
                    _pendingButton = 800007; // Return to Inbox
                }
            }
        }

        /// <summary>
        /// Reset state when leaving mail context.
        /// </summary>
        public static void Reset()
        {
            _lastState = -1;
            _emailIndex = 0;
            _emailCount = 0;
            _mailActive = false;
            _emailLines = null;
            _emailLineIndex = 0;
        }
    }
}
