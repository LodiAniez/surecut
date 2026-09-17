# SureCut

**One button. Your favorite programs. Always on top.**

SureCut is a tiny Windows 11 utility that puts a single round button in the corner of your
screen. Click it and a compact list of your favorite programs pops up. Click a program and it
opens. The button stays above every other window, so after you launch something that fills the
screen, the button is still right there for the next one.

It is not a dock and not a taskbar replacement. It does one thing: get you from "I want to open
X" to "X is open" in two clicks, without hunting through the Start menu.

<!-- screenshot: docs/screenshot.png -->

## Why you might want it

- You keep launching the same handful of programs and the Start menu is too many steps.
- Your taskbar is full, hidden, or on another monitor.
- You work in full-screen apps and want a launcher that is still reachable without minimizing.
- You want something that starts with Windows, remembers everything, and stays out of the way.

## Install

1. Go to the **[Releases page](https://github.com/LodiAniez/surecut/releases)** and download
   `SureCut.exe` from the latest release.
2. Move it somewhere permanent, for example `C:\Users\<you>\Apps\SureCut\SureCut.exe`.
   SureCut registers *that path* to start with Windows, so moving the file later means
   running it once from the new location.
3. Double-click `SureCut.exe`.

That is the whole install. There is no installer, no admin prompt, and no runtime to download:
the release build is a self-contained single file.

> **Windows SmartScreen** may warn that the file is from an unknown publisher the first time you
> run it. Choose *More info* → *Run anyway*. SureCut is not code-signed yet.

### Updating

SureCut checks the Releases page for a newer version shortly after it starts and every few hours.
When one exists, a small dot appears on the gear icon in the menu and the Settings panel shows a
**NEW** badge next to an **Update** button. Click **Update**: SureCut downloads the new
`SureCut.exe`, closes, replaces itself, and starts again with your favorites and settings intact.
You can also press **Check for updates** at any time, or turn the automatic check off in Settings.

To uninstall, quit SureCut (right-click the button → **Quit**), turn off *Start with Windows* in
Settings first if you had it on, and delete the `.exe`. Your settings live in
`%LOCALAPPDATA%\SureCut`; delete that folder too if you want a full clean-up.

## First-time setup

When SureCut starts for the first time, a round button with three lines appears at the
bottom-right of your main monitor, just above the taskbar. It has no favorites yet.

Add your programs in any of these ways:

- **Drag and drop** a program or shortcut (`.exe`, `.lnk`, `.bat`, `.cmd`) onto the button.
  Shortcuts from the Start menu (`%APPDATA%\Microsoft\Windows\Start Menu\Programs`) work well
  because they carry the right icon, arguments and working folder.
- **Click the button → gear icon → Add a program…** and pick a file.
- **Click the button** while the list is empty and use **Add a program…** there.

That is all the setup there is. Favorites, order and settings are saved immediately and survive
restarts.

## Using it day to day

| Action | How |
|---|---|
| Open or close the menu | Click the button, or press **Ctrl + Alt + Space** |
| Launch a program | Click its icon (or arrow to it and press **Enter** when opened with the shortcut) |
| See a program's name | Hover its icon for a moment |
| See more than five favorites | Click **Show more** at the bottom of the list |
| Reorder or remove | Right-click an icon → **Move up** / **Move down** / **Remove from favorites** |
| Open the folder a program lives in | Right-click an icon → **Open file location** |
| Move the button | Drag it anywhere, onto any screen |
| Settings | Click the gear at the bottom of the menu, or right-click the button |
| Quit | Right-click the button → **Quit** |

A few details worth knowing:

- **Clicking the button never steals focus.** If you are typing in a document, open the
  launcher, launch something, and your cursor is still where you left it. The program you launch
  comes to the front as usual.
- **Keyboard mode.** Opening with **Ctrl + Alt + Space** puts focus in the menu: **↑ / ↓** move,
  **→ / ←** show more or less, **Enter** launches, **Esc** closes and returns focus to where you were.
- **The menu opens toward the middle of the screen.** Park the button in any corner and the list
  and settings panel open away from the edges.
- **Greyed-out icon** means the program's file is missing (uninstalled or moved). Clicking it
  shows a short "Can't open" note instead of a dialog. Remove it or re-add the program.

## Settings

Open the gear at the bottom of the menu.

| Setting | What it does | Default |
|---|---|---|
| **Hide button when a program is launched** | After you launch something, the button disappears as soon as that program's window appears, and comes back when you close it. Press the shortcut to bring it back sooner. | Off |
| **Show or hide with a shortcut** | The global keyboard shortcut. Click the chip and press a new combination to rebind. If another program already owns that combination, SureCut says so and leaves it unbound. | Ctrl + Alt + Space |
| **Start with Windows** | Adds SureCut to your user's startup programs. No admin rights needed. | On |
| **Button size** | Small (40 px), Medium (48 px) or Large (56 px). Menu icons scale with it. | Medium |
| **Reset position** | Puts the button back at the bottom-right of the main monitor. | — |
| **Update / Check for updates** | Shows the installed version. When a newer release exists the button reads **Update** with a **NEW** badge; clicking it downloads and installs the new version and restarts SureCut. | — |
| **Check for updates automatically** | Asks GitHub for the latest release at startup and every few hours. Turn off to only check manually. | On |
| **Favorites** | Rename (click the name), remove (✕), and drag the grip to reorder. | — |

Everything follows your Windows theme: light or dark mode, accent color and transparency
changes apply live, no restart needed.

## Where SureCut keeps its data

| Location | Contents |
|---|---|
| `%LOCALAPPDATA%\SureCut\config.json` | All settings and favorites. Plain JSON, safe to back up or edit while SureCut is closed. |
| `%LOCALAPPDATA%\SureCut\icons\` | Cached program icons, so the menu renders even if a drive is temporarily offline. |
| `%LOCALAPPDATA%\SureCut\log.txt` | A small rolling log, useful when reporting a problem. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SureCut` | The *Start with Windows* entry. Removed when you turn the setting off. |

SureCut has no accounts, no cloud sync and no telemetry. The only network request it makes is
the optional update check, which asks GitHub's public releases feed for the latest version and
sends nothing about you or your favorites. Turn it off in Settings if you prefer.

## Troubleshooting

**The shortcut does nothing.** Another program has probably claimed Ctrl + Alt + Space. Open
Settings; if the chip is shown in italics with a warning, pick a different combination.

**The button disappeared.** If *Hide button when a program is launched* is on, it is waiting
for the launched program to close. Press the shortcut to show it now. If SureCut is not running
at all, start it again from the `.exe`; running a second copy just brings back the existing one.

**The button is on the wrong monitor or off-screen after changing displays.** SureCut snaps
back to the default position automatically when its saved spot no longer exists. If it looks
lost, right-click it → **Reset position**, or press the shortcut and use the gear.

**Something else went wrong.** Look at `%LOCALAPPDATA%\SureCut\log.txt` and
[open an issue](https://github.com/LodiAniez/surecut/issues) with the relevant lines. If the
config file ever becomes unreadable, SureCut backs it up as `config.json.bak` and starts fresh
rather than crashing.

## Requirements

- Windows 11 (the translucent menu surface needs 22H2 or newer; on older builds and on
  Windows 10 SureCut falls back to a solid surface).
- No .NET installation required for the release build.
- No administrator rights.

## Known limitations

- SureCut cannot appear above exclusive-fullscreen games or video.
- Folders and web links are not accepted as favorites yet; only programs and shortcuts.
- The release build is a self-contained single file, so it is large for what it does (about
  60 MB on disk) and uses more memory than a framework-dependent build would. A lighter build is
  being evaluated.
- Not yet code-signed, hence the SmartScreen warning.

---

## For developers

Requires the .NET 8 SDK on Windows.

```powershell
# debug build and run
dotnet build src\SureCut\SureCut.csproj -c Debug
.\src\SureCut\bin\Debug\net8.0-windows\SureCut.exe

# portable single-file release build → publish\SureCut.exe
dotnet publish src\SureCut\SureCut.csproj -c Release -o publish
```

Project layout:

```
src/SureCut/
  App.xaml(.cs)          entry point: single instance, logging, crash-loop guard
  LauncherHost.cs        controller: owns the windows and services, implements the behaviors
  Windows/
    ButtonWindow         the floating button: layered, never activates, hold-to-drag, drop target
    MenuWindow           the favorites menu: DWM Acrylic; mouse mode keeps focus elsewhere,
                         shortcut mode is keyboard-driven
    SettingsWindow       settings panel beside the menu
  Services/              config store, icon cache, hotkey, low-level input hooks, launch tracker,
                         monitor/anchor math, launcher, startup (Run key), theme, single instance,
                         update service (GitHub releases check, download, self-replace helper)
  Interop/               P/Invoke declarations and window-styling helpers
  Themes/Styles.xaml     Fluent-style control templates and default palette
```

Implementation notes that are easy to trip over:

- The button and a mouse-opened menu use `WS_EX_NOACTIVATE` and reply `MA_NOACTIVATE` to
  `WM_MOUSEACTIVATE`; without the reply the first click on the window is lost. Esc and outside
  clicks are caught with low-level hooks that exist only while the menu is open.
- `AllowSetForegroundWindow(ASFW_ANY)` is called right before `ShellExecuteEx` so the launched
  program can take the foreground even though SureCut never had it.
- Hide-on-launch tracks the launched program's *window*, not its process, because many apps
  (browsers, Store apps) start through a stub process that exits immediately.
- WPF's `SizeToContent` kept snapping the small windows back to their creation width, so the
  windows size themselves from measured content and are only *moved* with `SetWindowPos`.
- Do not enable `InvariantGlobalization`; WPF text input resolves the input language by LCID and
  throws under invariant mode.
