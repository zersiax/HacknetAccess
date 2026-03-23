# HacknetAccess

A screen reader accessibility mod for [Hacknet](https://store.steampowered.com/app/365450/Hacknet/) (2015). Makes the game fully playable for blind and visually impaired users by announcing all UI elements, game state changes, and providing keyboard navigation for every interface.

Built on [Hacknet-Pathfinder](https://github.com/Arkhist/Hacknet-Pathfinder) (BepInEx-based mod loader) using Harmony patches.

## Features

- Full keyboard navigation for all game interfaces — no mouse required
- Screen reader announcements via [Tolk](https://github.com/ndarilek/tolk) (supports NVDA, JAWS, and other screen readers)
- Terminal output reading and line-by-line navigation
- Word-by-word navigation and clipboard copy support
- Accessible main menu, settings, login/account creation
- Mail inbox with attachment navigation
- Network map browsing
- RAM status monitoring
- Display module focus mode for daemon interaction
- Daemon support: mail servers, databases, IRC, web servers, message boards, mission boards, contract hubs
- Notifications: incoming mail, connections, trace countdown
- Generic exe module announcements (start, progress at 25/50/75%, completion) for all port crackers and tools
- Accessible tutorial with keyboard hints replacing mouse-only instructions
- All strings localization-ready via centralized Loc.cs

## Keyboard Shortcuts

- F1: Help (list all shortcuts)
- F2: Re-read terminal output
- F3: Network map (Up/Down navigate, Enter connect, Escape back)
- F4: Read RAM status
- F5: Display focus mode (daemon interaction). Up/Down/Enter/Escape when focused. Press again to re-read. Escape returns to terminal.
- F6: Open mail (bare Up/Down/Enter navigate inbox and emails)
- F7: Notes focus mode (Up/Down navigate, Escape exit, Ctrl+Shift+W close)
- F12: Toggle debug mode
- Ctrl+R: Repeat last announcement
- Ctrl+C: Copy navigated line or word to clipboard
- Ctrl+Up/Down: Navigate terminal output lines
- Ctrl+Left/Right: Navigate words within current line
- Ctrl+O: Overload proxy from all shells
- Ctrl+T: Set/trigger trap from all shells

## Installation

### Prerequisites

- [Hacknet](https://store.steampowered.com/app/365450/Hacknet/) (Steam version)
- [Hacknet-Pathfinder v5.3.4](https://github.com/Arkhist/Hacknet-Pathfinder) — follow its installation instructions to patch the game first

### Install the mod

1. Download the latest release from the [Releases](../../releases) page
2. Copy `HacknetAccess.dll` to your Hacknet `BepInEx\plugins\` folder (typically `C:\Program Files (x86)\Steam\steamapps\common\Hacknet\BepInEx\plugins\`)
3. Copy `Tolk.dll` and `nvdaControllerClient32.dll` to your Hacknet game directory (next to `Hacknet.exe`)
4. Launch Hacknet — the mod will announce "HacknetAccess loaded" on startup

### Important: 32-bit DLLs required

Hacknet is a 32-bit application. The Tolk.dll and nvdaControllerClient32.dll included in the release are specifically the 32-bit versions. **Standard 64-bit Tolk and nvdaControllerClient DLLs will not work.** These 32-bit builds are difficult to find online, which is why they are included in the release.

- `Tolk.dll` — 32-bit screen reader bridge library
- `nvdaControllerClient32.dll` — required for NVDA support (JAWS works through COM without an extra DLL)

## Building from source

Requirements: .NET SDK (any recent version that supports targeting .NET Framework 4.7.2)

1. Clone this repo
2. Ensure Hacknet and Hacknet-Pathfinder are installed at the default Steam path, or update the HintPath references in `src/HacknetAccess.csproj`
3. Run: `dotnet build src/HacknetAccess.csproj`
4. The build automatically deploys `HacknetAccess.dll` to the game's plugins folder

## Architecture

```
Plugin.cs                         Entry point, F-key input polling, F1 help
AccessStateManager.cs             Tracks active UI context
DebugLogger.cs                    Categorized logging via BepInEx
ReflectionHelper.cs               Private field access
ScreenReader.cs                   Tolk wrapper
Loc.cs                            All screen reader strings (localization-ready)

Patches/
  InputPatches.cs                 Game update loop, terminal input suppression
  MainMenuPatches.cs              Menu button focus tracking
  LoginScreenPatches.cs           Login/account screen
  MessageBoxPatches.cs            Modal dialogs
  TerminalPatches.cs              Terminal output, line/word nav, prompt polling
  DisplayModulePatches.cs         Display modes, daemon focus mode
  NotificationPatches.cs          Mail, trace, incoming connection
  OptionsMenuPatches.cs           Settings menu keyboard navigation
  NetworkMapPatches.cs            Network map browsing
  RamModulePatches.cs             RAM status
  DaemonPatches.cs                IRC, web server
  DatabaseDaemonPatches.cs        Academic/medical databases
  MissionListingPatches.cs        Faction mission boards
  MissionHubPatches.cs            CSEC contract hub
  MessageBoardPatches.cs          Image board threads
  MailPatches.cs                  Email inbox, viewer, reply, attachments
  NotesPatches.cs                 Notes app focus mode
  ExeModulePatches.cs             Port crackers, shells, generic exe progress
  TutorialPatches.cs              Tutorial accessibility hints
  IntroTextPatches.cs             Intro sequence
  TraceDangerPatches.cs           Trace danger warnings
```

## License

MIT License. See [LICENSE](LICENSE).
