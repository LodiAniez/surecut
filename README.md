# SureCut

A one-button floating launcher for Windows 11. A small round button sits above the taskbar,
always on top. Click it (or press `Ctrl+Alt+Space`) to open an icons-only menu of your
favorite programs; click one to launch it. Nothing else gets in the way.

## Build and run

Requires the .NET 8 SDK on Windows 10/11.

```powershell
dotnet build src\SureCut\SureCut.csproj -c Debug
.\src\SureCut\bin\Debug\net8.0-windows\SureCut.exe
```

There is no main window. The button appears bottom-right; right-click it for **Settings**,
**Reset position** and **Quit**.

### Portable single-file build

```powershell
dotnet publish src\SureCut\SureCut.csproj -c Release -o publish
```

`publish\SureCut.exe` is self-contained (no runtime install, no admin rights).

## Where things live

| Path | Purpose |
|---|---|
| `%LOCALAPPDATA%\SureCut\config.json` | All settings and favorites (written atomically, debounced) |
| `%LOCALAPPDATA%\SureCut\icons\` | Cached program icons (32 px and 64 px PNGs) |
| `%LOCALAPPDATA%\SureCut\log.txt` | Rolling log (1 MB) |
| `HKCU\...\CurrentVersion\Run\SureCut` | Start-with-Windows entry (on by default, removable in Settings) |

Delete the folder to reset everything. A corrupt `config.json` is backed up as `config.json.bak`.

## Project layout

```
src/SureCut/
  App.xaml(.cs)          entry point: single instance, logging, crash-loop guard
  LauncherHost.cs        controller: owns windows + services, implements the behaviors
  Windows/
    ButtonWindow         the floating button (layered, never activates, hold-to-drag, drop target, hotkey host)
    MenuWindow           the favorites menu (DWM Acrylic, mouse mode = no focus, hotkey mode = keyboard)
    SettingsWindow       settings panel beside the menu
  Services/
    ConfigStore          JSON persistence          IconCache        shell icon extraction + PNG cache
    HotkeyService        RegisterHotKey            InputHooks       low-level Esc / outside-click hooks
    LaunchTracker        finds the launched program's window and watches for it closing
    Monitors             monitor enumeration + anchor/offset position math
    ProgramLauncher      ShellExecuteEx + foreground handoff
    StartupService       Run key                   ThemeService     light/dark/accent, live
    SingleInstance       mutex + broadcast message  Logger
  Interop/
    NativeMethods        P/Invoke declarations
    WindowStyling        tool-window / no-activate / topmost / backdrop / sizing helpers
  Themes/Styles.xaml     Fluent-style control templates and the default palette
  Controls/ErrorBubble   inline "Can't open" bubble
```

## Behavior notes worth knowing

- **Focus.** The button and a mouse-opened menu use `WS_EX_NOACTIVATE` and answer
  `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, so the program you were typing in keeps focus.
  Esc and outside clicks are detected with low-level hooks installed only while the menu is open.
  A hotkey-opened menu *is* activated, so arrow keys, Enter and Esc work natively, and focus is
  handed back when it closes.
- **Launching.** `AllowSetForegroundWindow(ASFW_ANY)` is called right before `ShellExecuteEx`
  so the launched program can come to the front even though the launcher never had focus.
- **Hide on launch.** Instead of watching the launched *process* (many apps are stubs that exit
  immediately), the tracker watches for the next foreground window from a new process, hides the
  button, and shows it again when that window is destroyed. Ten seconds without a window means
  the button stays.
- **Sizing.** WPF's `SizeToContent` kept snapping the tiny windows back to their creation width,
  so the windows size themselves from their content's `DesiredSize` and only *move* via
  `SetWindowPos`. `WM_GETMINMAXINFO` is also answered to lift the OS minimum width.
- **Do not enable `InvariantGlobalization`.** WPF text input resolves the current input
  language by LCID and throws under invariant mode.
