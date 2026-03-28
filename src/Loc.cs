using System.Collections.Generic;

namespace HacknetAccess
{
    /// <summary>
    /// Localization system for all screen reader strings.
    /// All user-facing text must go through Loc.Get().
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, string> _strings = new Dictionary<string, string>
        {
            // General
            { "mod.loaded", "HacknetAccess loaded." },
            { "mod.version", "HacknetAccess version {0}" },
            { "debug.toggle", "Debug mode {0}" },
            { "game.started", "Game started. Terminal ready." },
            { "game.saved", "Session saved." },

            // Intro
            { "intro.complete", "Intro complete." },

            // Tutorial
            { "tutorial.step", "Tutorial: {0}" },
            { "tutorial.pressContinue", "Press Enter to continue." },
            { "tutorial.hint.connectSelf", "Press F3 to open the network map, then use Up/Down and Enter to select your computer. Or type connect followed by your IP." },
            { "tutorial.hint.scan", "Type scan in the terminal." },
            { "tutorial.hint.connectOther", "Press F3 to open the network map, navigate to another node with Up/Down, and press Enter. Or type connect followed by the IP." },

            // Main Menu
            { "menu.main", "Main Menu" },
            { "menu.item", "{0}, {1} of {2}" },
            { "menu.newSession", "New Session" },
            { "menu.continue", "Continue" },
            { "menu.login", "Login" },
            { "menu.settings", "Settings" },
            { "menu.extensions", "Extensions" },
            { "menu.dlcSession", "New Labyrinths Session" },
            { "menu.exit", "Exit" },
            { "menu.options", "Settings" },

            // Login/Account Screen
            { "login.newAccount", "New account registration" },
            { "login.existingAccount", "Login" },
            { "login.prompt", "{0}" },
            { "login.promptPassword", "{0} (password, text hidden)" },

            // Dialog / Message Box
            { "dialog.message", "{0}. {1} or {2}" },

            // Terminal
            { "terminal.output", "{0}" },
            { "terminal.prompt", "Terminal ready." },
            { "terminal.empty", "No recent terminal output." },
            { "terminal.line", "Line {0} of {1}: {2}" },
            { "terminal.word", "Word {0} of {1}: {2}" },
            { "terminal.copied", "Copied: {0}" },
            { "terminal.promptInput", "{0}" },
            { "terminal.promptPassword", "{0} (password, text hidden)" },

            // Tab Completion
            { "tab.noMatch", "No completions." },
            { "tab.completed", "Completed: {0}" },
            { "tab.multiple", "{0} matches. Type more to narrow down. Current: {1}" },

            // Display Module
            { "display.ls", "{0}" },
            { "display.cat", "{0}" },
            { "display.connect", "{0}" },
            { "display.probe", "{0}" },
            { "display.login", "{0}" },
            { "display.empty", "Display is empty." },
            { "display.focused", "Display focused. Up/Down to navigate, Enter to select, Escape for terminal." },
            { "display.terminalFocused", "Terminal focused." },
            { "display.focusLost", "Display focus lost." },

            // Notifications
            { "notify.mail", "New email received." },
            { "notify.incomingConnection", "Warning: Incoming connection detected!" },
            { "notify.traceStarted", "Trace detected! {0} seconds." },
            { "notify.tracePercent", "Trace: {0} percent remaining." },
            { "notify.traceCritical", "Trace critical! Under 10 percent!" },

            // Network Map
            { "network.header", "Network Map: {0} nodes visible" },
            { "network.node", "{0}, {1}, {2} of {3}{4}" },
            { "network.focusTerminal", "Terminal focused." },
            { "network.instructions", "Up/Down to navigate, Enter to connect, Escape for terminal." },
            { "network.newNodes", "{0} new node(s) discovered." },
            { "network.unavailable", "Network map not available." },

            // RAM Module
            { "ram.status", "RAM: {0} MB used of {1} MB total" },
            { "ram.warning", "Warning: Out of memory!" },
            { "ram.unavailable", "RAM status not available." },

            // Mail
            { "mail.server", "Mail server. Enter to login, Escape to exit." },
            { "mail.inbox", "Inbox: {0} emails. Up/Down to navigate, Enter to open." },
            { "mail.empty", "Inbox is empty." },
            { "mail.item", "{0} of {1}. {2}{3}, {4}" },
            { "mail.unread", "Unread. " },
            { "mail.attachment", "Attachment {0}: {1}" },
            { "mail.attachmentActivated", "Activated: {0}" },
            { "mail.reply", "Reply screen. Ctrl+D to add detail text, Enter to send, Escape to go back." },
            { "mail.unavailable", "Mail not available." },

            // Notes
            { "notes.added", "Note saved: {0}" },
            { "notes.empty", "Notes app is not running." },
            { "notes.header", "{0} notes:" },
            { "notes.item", "Note {0} of {1}: {2}" },
            { "notes.closed", "Notes closed." },
            { "notes.notRunning", "Notes program is not running." },

            // Daemons — general
            { "daemon.loginSuccess", "Login successful." },
            { "daemon.webPage", "Web page: {0}" },
            { "daemon.ircMessage", "{0}" },
            { "daemon.mailFrom", "From: {0}" },
            { "daemon.mailSubject", "Subject: {0}" },

            // Message Board
            { "board.title", "{0}. {1} threads. Up/Down to navigate, Enter to read, Escape for terminal." },
            { "board.thread", "Thread {0} of {1}: {2}" },
            { "board.threadView", "{0}" },
            { "board.back", "Escape to go back." },

            // Exe Modules
            { "exe.porthackProgress", "Porthack: {0} percent" },
            { "exe.porthackComplete", "Porthack complete. Admin access on {0}" },
            { "exe.shellOpened", "Shell opened on {0}" },
            { "exe.shellComplete", "Shell closed on {0}" },
            { "exe.shellOverloadStart", "Overloading proxy on {0}" },
            { "exe.shellOverloadDone", "Proxy overloaded on {0}" },
            { "exe.shellTrapDone", "Trap triggered on {0}." },
            { "exe.traceKillDone", "TraceKill finished" },
            { "exe.noShells", "No shells active." },
            { "exe.closeAll", "Closing {0} shell(s)." },
            { "exe.overloadAll", "Overloading proxy from {0} shell(s)." },
            { "exe.trapAll", "Setting trap on {0} shell(s). Ctrl+T again to trigger when ready." },
            { "exe.trapLoading", "{0} trap(s) still loading RAM. Wait a moment." },
            { "exe.trapsReady", "{0} trap(s) ready! Press Ctrl+T to trigger." },
            { "exe.triggerAll", "Triggering {0} trap(s)." },
            { "exe.opponentOnTrap", "Opponent on {0}! Trap set — press Ctrl+T to trigger NOW!" },
            { "exe.opponentMoved", "Opponent moved to {0}." },
            { "exe.opponentGone", "Opponent disconnected." },
            { "exe.genericStarted", "{0} running." },
            { "exe.genericProgress", "{0}: {1} percent." },
            { "exe.genericComplete", "{0} complete." },

            // Trace Danger
            { "trace.danger", "CRITICAL: Trace complete! Disconnect and reboot!" },
            { "trace.countdown", "Trace danger: {0} seconds remaining" },
            { "trace.ipReset", "IP reset successful. Rebooting." },
            { "trace.gameover", "System destroyed. Game over." },
            { "trace.pressBegin", "Trace danger! Press Enter to begin recovery." },

            // Mission Hub
            { "hub.welcome", "Contract Hub: {0}. Enter to login, Escape to exit." },
            { "hub.menu", "Hub Menu. Ctrl+Enter for contracts, Ctrl+U for users, Ctrl+A to abort, Escape to exit." },
            { "hub.listing", "Contract Listing: {0} contracts. Ctrl+Up/Down to navigate, Ctrl+Enter to open." },
            { "hub.mission", "Contract {0} of {1}: {2}" },
            { "hub.preview", "{0}. {1}. Ctrl+Enter to accept, Escape to go back." },
            { "hub.accept", "Contract accepted." },
            { "hub.cancel", "Abandon current contract? Ctrl+Enter to confirm, Escape to go back." },
            { "hub.noMissions", "No contracts available." },

            // Mission Listing (faction boards — missions, articles, or posts)
            { "listing.boardMission", "{0}: {1} contracts. Ctrl+Up/Down to navigate, Ctrl+Enter to open." },
            { "listing.boardArticle", "{0}: {1} articles. Up/Down to navigate, Enter to read." },
            { "listing.itemMission", "Contract {0} of {1}: {2}" },
            { "listing.itemArticle", "Article {0} of {1}: {2}" },
            { "listing.detail", "{0}. {1}." },
            { "listing.accepted", "Contract accepted." },
            { "listing.needLogin", "{0}. Enter to login, Escape to exit." },
            { "listing.hasActiveMission", "Complete or abandon current contract first." },
            { "listing.abandonHint", "Ctrl+A to abandon current contract." },
            { "listing.acceptHint", "Ctrl+Enter to accept." },
            { "listing.wrongFaction", "Contract unavailable: assigned to different faction." },
            { "listing.back", "Escape to go back." },

            // Daemon Login
            { "login.knownUsers", "{0} known accounts. Up/Down to select, Enter to login." },
            { "login.noKnownUsers", "No known accounts. You need to discover credentials first." },
            { "login.user", "Account {0} of {1}: {2}, password {3}" },
            { "login.failed", "Login failed. Enter to retry, Escape to go back." },

            // Database Daemons
            { "db.welcome", "Database server. Enter to search, Escape to exit." },
            { "db.search", "Enter search term." },
            { "db.entry", "Record: {0}" },
            { "db.degree", "Degree: {0}" },
            { "db.notFound", "No results found." },
            { "db.multiMatch", "{0} matches found. Up/Down to navigate, Enter to select." },
            { "db.matchItem", "Match {0} of {1}: {2}" },

            // Help (F1)
            { "help.title", "HacknetAccess Keyboard Shortcuts" },
            { "help.f1", "F1: This help" },
            { "help.f2", "F2: Re-read terminal output. Ctrl+Up/Down to navigate lines." },
            { "help.f3", "F3: Network map. Up/Down navigate, Enter connect, Escape back." },
            { "help.f4", "F4: Read RAM status" },
            { "help.f5", "F5: Focus display panel (daemon interaction). Press again to re-read." },
            { "help.f6", "F6: Open mail" },
            { "help.f7", "F7: Read notes. Up/Down to navigate. Escape to exit. Ctrl+Shift+W to close." },
            { "help.f9", "F9: Save session" },
            { "help.f10", "F10: Open settings" },
            { "help.f12", "F12: Toggle debug mode" },
            { "help.ctrlr", "Ctrl+R: Repeat last announcement" },
            { "help.ctrlc", "Ctrl+C: Copy navigated line or word to clipboard" },
            { "help.ctrlupdown", "Ctrl+Up/Down: Navigate terminal output lines" },
            { "help.ctrlleftright", "Ctrl+Left/Right: Navigate words within current line" },
            { "help.ctrlhomeend", "Ctrl+Home/End: Jump to first/last terminal output line" },
            { "help.ctrlo", "Ctrl+O: Overload proxy from all shells" },
            { "help.ctrlw", "Ctrl+W: Close all shells" },
            { "help.ctrlt", "Ctrl+T: Set trap on idle shells. Press again to trigger when ready. Announces when loaded." },
            { "help.ctrlenter", "Ctrl+Enter: Spell out current line or word character by character" },

            // Settings Menu
            { "settings.unavailable", "Settings not available during trace." },
            { "settings.opened", "Settings menu. {0} controls. Up/Down to navigate, Enter to activate." },
            { "settings.control", "{0}, {1}, {2} of {3}" },
            { "settings.toggled", "{0}: {1}" },
            { "settings.listMode", "{0}: selecting. Up/Down to browse, Enter to confirm, Escape to cancel." },
            { "settings.listItem", "{0} of {1}: {2}" },
            { "settings.sliderMode", "{0}: adjusting. Left/Right to change, Enter or Escape to finish." },
            { "settings.selected", "{0} set to {1}" },
            { "settings.applied", "Changes applied." },
            { "settings.volume", "Volume: {0} percent" },
        };

        /// <summary>
        /// Gets a localized string by key, with optional format arguments.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            if (!_strings.TryGetValue(key, out string value))
            {
                Plugin.Instance?.Log.LogWarning($"Missing localization key: {key}");
                return $"[{key}]";
            }

            if (args != null && args.Length > 0)
            {
                return string.Format(value, args);
            }

            return value;
        }
    }
}
