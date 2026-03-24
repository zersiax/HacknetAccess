# HacknetAccess

A screen reader accessibility mod for [Hacknet](https://store.steampowered.com/app/365450/Hacknet/) (2015). Makes the game fully playable for blind and visually impaired users by announcing all UI elements, game state changes, and providing keyboard navigation for every interface.

Built on [Hacknet-Pathfinder](https://github.com/Arkhist/Hacknet-Pathfinder) (BepInEx-based mod loader) using Harmony patches.

## Features

- Full keyboard navigation for all game interfaces — no mouse required
- Screen reader announcements via [Tolk](https://github.com/ndarilek/tolk) (supports NVDA, JAWS, and other screen readers)
- Terminal output reading and line-by-line navigation
- Word-by-word navigation, clipboard copy, and character-by-character spell out
- Accessible main menu, settings, login/account creation
- Mail inbox with full keyboard navigation, reply, and attachment support
- Network map browsing
- Reading notes in the notes app
- RAM status monitoring
- Display module focus mode for daemon interaction
- Daemon support: mail servers, databases, IRC, web servers, message boards, mission boards, contract hubs
- Daemon login via known-credential picker (discovered usernames/passwords from emails and comp.users)
- Notifications: incoming mail, connections, trace countdown
- Generic exe module announcements (start, progress at 25/50/75%, completion) for all port crackers and tools
- Accessible tutorial with keyboard hints replacing mouse-only instructions
- All strings localization-ready via centralized Loc.cs

## Keyboard Shortcuts

### Global

- F1: Help (list all shortcuts)
- F2: Re-read terminal output
- F3: Network map (Up/Down navigate, Enter connect, Escape back)
- F4: Read RAM status
- F5: Display focus mode (daemon interaction). Up/Down/Enter/Escape when focused. Press again to re-read. Escape returns to terminal.
- F6: Open mail inbox
- F7: Notes focus mode (Up/Down navigate, Escape exit, Ctrl+Shift+W close). Due to the way notes work its best to close when you don't need it open, as it eats RAM.
- F12: Toggle debug mode
- Ctrl+R: Repeat last announcement
- Ctrl+O: Overload proxy from all shells
- Ctrl+T: Set/trigger trap from all shells

### Terminal Review Buffer

- Ctrl+Up/Down: Navigate terminal output lines
- Ctrl+Left/Right: Navigate words within current line
- Ctrl+C: Copy navigated line or word to clipboard (depends on whether you last read a line or a word — this is a screen reader convenience; the game does not natively support this)
- Ctrl+Enter: Spell out the current line or word character by character (useful for reading IP addresses, filenames, etc.)

### Mail

- F6: Open mail inbox
- Up/Down: Navigate emails in inbox
- Enter: Open selected email
- Up/Down: Navigate lines within an open email
- Escape: Close email / exit mail
- Ctrl+R: Open reply screen (when viewing an email with a reply option)
- Ctrl+D: Add detail text to reply
- Enter: Send reply
- Left/Right: Navigate attachments on an email, although they also show up as lines below the end of the text.
- Enter: Activate selected attachment

### Daemon Login (Mission Boards, Contract Hubs)

When a daemon requires login, the mod presents a known-credential picker instead of requiring manual terminal input:

- Up/Down: Navigate known accounts (discovered from emails and comp.users files)
- Enter: Login with selected account
- Escape: Go back

### Display Focus (F5)

- Up/Down: Navigate items (contracts, articles, search results)
- Enter: Select / open / accept
- Escape: Go back or return to terminal
- Ctrl+Up/Down: Navigate contracts on mission boards and hubs
- Ctrl+Enter: Open contract detail or accept contract

## Installation

### Prerequisites

- [Hacknet](https://store.steampowered.com/app/365450/Hacknet/) (Steam version)
- [Hacknet-Pathfinder v5.3.4](https://github.com/Arkhist/Hacknet-Pathfinder)
- A screen reader (NVDA, JAWS, or any Tolk-compatible reader)

### Quick install (recommended, fully accessible)

A PowerShell script is included that handles everything — Pathfinder setup, executable renaming for Steam compatibility, and mod file installation:

1. Download the latest release from the [Releases](../../releases) page and extract the zip
2. Open PowerShell in the extracted folder
3. Run: `.\scripts\Install-HacknetAccess.ps1`
4. Launch Hacknet from Steam as normal — the mod will announce "HacknetAccess loaded" on startup

The script accepts a `-GameDir` parameter if your game is not in the default Steam location:
```
.\Install-HacknetAccess.ps1 -GameDir "D:\Games\Hacknet"
```

### Manual install

If you prefer to install manually, follow these steps.

#### Step 1: Patch the game with Pathfinder

Pathfinder must patch the Hacknet executable before mods can load:

1. Download `Pathfinder.Release.zip` from the [Pathfinder releases page](https://github.com/Arkhist/Hacknet-Pathfinder/releases)
2. Extract it into your Hacknet game directory (typically `C:\Program Files (x86)\Steam\steamapps\common\Hacknet`)
3. Open a command prompt or PowerShell in the game directory and run:
   ```
   .\PathfinderPatcher.exe Hacknet.exe
   ```
4. This creates `HacknetPathfinder.exe` — a patched copy of Hacknet that loads BepInEx and mods

**Note:** Pathfinder also offers a GUI installer, but it is not screen reader accessible and would require OCR to use.

#### Step 2: Enable Steam launch (recommended)

By default, Pathfinder creates a separate `HacknetPathfinder.exe`. Launching Hacknet through Steam would still run the unmodded `Hacknet.exe`. To fix this, rename the executables so Steam launches the modded version:

```
ren Hacknet.exe HacknetOld.exe
ren HacknetPathfinder.exe Hacknet.exe
```

After this, Steam will launch the modded version directly. The original is preserved as `HacknetOld.exe`.

To restore the original game, either reverse the renames or use Steam's "Verify integrity of game files" option.

**Alternative:** If you prefer not to rename, you can launch `HacknetPathfinder.exe` directly from the game directory. Steam features like playtime tracking will still work if you launch while Steam is running.

#### Step 3: Install mod files

1. Download the latest release from the [Releases](../../releases) page
2. Copy `HacknetAccess.dll` to `BepInEx\plugins\` in the game directory
3. Copy `Tolk.dll` and `nvdaControllerClient32.dll` to the game directory (next to the exe)
4. Copy `HacknetPathfinder.exe.config` to the game directory — rename it to match whatever the patched exe is called (e.g. `Hacknet.exe.config` if you did the rename in step 2). This prevents Windows from blocking downloaded DLLs.
5. Launch the game — the mod will announce "HacknetAccess loaded" on startup

### Important: 32-bit DLLs required

Hacknet is a 32-bit application. The Tolk.dll and nvdaControllerClient32.dll included in the release are specifically the 32-bit versions. **Standard 64-bit Tolk and nvdaControllerClient DLLs will not work.** These 32-bit builds are difficult to find online, which is why they are included in the release.

- `Tolk.dll` — 32-bit screen reader bridge library
- `nvdaControllerClient32.dll` — required for NVDA support (JAWS works through COM without an extra DLL)

## Building from source

Requirements: .NET SDK (any recent version that supports targeting .NET Framework 4.7.2)

1. Clone this repo
2. Ensure Hacknet and Hacknet-Pathfinder are installed at the default Steam path, or update the HintPath references in `src/HacknetAccess.csproj`
3. Run: `dotnet build src/HacknetAccess.csproj`
4. The build automatically deploys `HacknetAccess.dll` to the game's plugins folder and copies release files to `release/`

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
