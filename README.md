# Brightness Tray

A lightweight Windows system-tray app for controlling the brightness of every connected display — the laptop's built-in panel and external monitors — and for switching an external monitor's input source (HDMI, DisplayPort, USB-C, …) so you can flip which computer it is showing. It is modelled on [Twinkle Tray](https://twinkletray.com/), written from scratch as a single native .exe with no installer and no runtime to download.

## Contents

- [Quick start](#quick-start)
- [Features at a glance](#features-at-a-glance)
- [User experience](#user-experience)
- [UI design](#ui-design)
- [Technical architecture](#technical-architecture)
- [Source code map](#source-code-map)
- [Building](#building)
- [Configuration and files on disk](#configuration-and-files-on-disk)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)

## Quick start

```powershell
.\build.ps1            # compiles bin\BrightnessTray.exe (and generates the app icon on first run)
.\install-shortcut.ps1 # adds "Brightness Tray" to the Start menu
```

Then launch **Brightness Tray** from the Start menu. A sun icon appears in the notification area and the brightness panel opens. Right-click the icon and tick **Start with Windows** to have it load silently at every sign-in.

**Requirements**: Windows 10 or 11 with .NET Framework 4.x (built into Windows). Nothing else.

## Features at a glance

| Feature | How to use it |
| :--- | :--- |
| **Per-display brightness sliders** | Left-click the tray icon to open the panel |
| **Scroll-to-adjust on the tray icon** | Hover over the sun icon and turn the mouse wheel |
| **Input source switching** | Click an input button (HDMI 1, DisplayPort 1, USB-C …) under a monitor |
| **Link all displays** | Tick Link all displays in the panel or the tray menu |
| **Built-in laptop panel support** | Automatic (uses Windows' own brightness control) |
| **External monitor support** | Automatic over DDC/CI |
| **Live brightness indicator** | Tray icon's sun fills up with brightness, and hovering shows a tooltip with each display's level |
| **Adjustable scroll step** | Tray menu → Scroll step → 1 / 2 / 5 / 10 / 20 % |
| **Show all common inputs** | Tray menu option for monitors that misreport their inputs |
| **Turn off displays** | Tray menu → Turn off displays |
| **Start with Windows** | Tray menu toggle; starts silently in the tray |
| **Hot-plug and sleep awareness** | Re-detects displays automatically when monitors change or the PC resumes |
| **Start menu entry** | Launching again opens the panel of the already-running copy |
| **Light/dark taskbar aware** | Tray icon recolours itself to match the taskbar theme |
| **Windows accent colour** | Sliders and selected buttons use your personal accent colour |

## User experience

### The tray icon

Brightness Tray lives entirely in the notification area (system tray). The icon is a small sun whose core fills from the bottom up in quarter steps (empty, ¼, ½, ¾, full) to reflect the average brightness of your displays, so you can tell roughly how bright things are without opening anything.

- **Hover** to get a tooltip listing every display and its current level, e.g. `Brightness` / `Built-in Display: 70%` / `DELL U2720Q: 45%`.
- **Scroll the mouse wheel** over the icon to make everything brighter or dimmer by the scroll step (default 5 %). The wheel event is consumed, so the window under the taskbar does not also scroll. High-resolution / smooth-scrolling wheels are accumulated into whole notches so one "click" always equals one step.
- **Left-click** to toggle the brightness panel.
- **Right-click** for the menu.

> **Tip**: if Windows has tucked the icon into the hidden-icons overflow (the `^` arrow), drag it onto the taskbar. Scroll-to-adjust works best when the icon is always visible.

### The brightness panel (flyout)

The panel pops up just above the taskbar, horizontally centred on the tray icon, in the style of the Windows 11 quick-settings flyouts. It behaves like a native flyout:
- It closes when you click anywhere else, press `Esc`, or click the tray icon again.
- It never appears in the taskbar or in Alt+Tab.
- It works with the taskbar on the bottom, top or left of the screen.
- Each time it opens it re-reads the actual brightness and active input from the hardware in the background. If you changed something with the monitor's own buttons, the panel catches up within a moment of opening.

For each display, from left to right in the same order as your physical screen arrangement, the panel shows:
1. **Name**, using the monitor's real model name (e.g. `DELL U2720Q`) or `Built-in Display` for a laptop panel. Identical monitors are numbered `(1)`, `(2)`.
2. **Control type tag** on the right: `Built-in`, `DDC/CI`, or `Not supported`.
3. **Brightness slider** with the numeric value next to it. You can:
   - drag or click anywhere on the track;
   - scroll the wheel while hovering (the slider takes focus on hover, no click needed);
   - use the keyboard: `←` / `→` or `↑` / `↓` for ±1, `Page Up` / `Page Down` for ±10, `Home` / `End` for 0 / 100.
4. **Input source buttons** (external monitors only): a wrapping row of pill-shaped buttons, one per input the monitor reports, with the active input highlighted in your accent colour. Clicking one switches the monitor immediately. While the monitor is still being queried, the caption reads `Input source (reading monitor…)`.
5. **Help text** (unsupported monitors only), explaining that DDC/CI must be enabled in the monitor's on-screen menu, or that a dock or DisplayLink adapter may be blocking it.

The footer contains:
- **Link all displays**: when ticked, moving any slider (or scrolling the tray icon) sets every display to the same level.
- **Refresh**: re-detects all displays from scratch, useful after plugging something in.

Brightness changes are applied live while you drag. Monitors are slow to accept commands, so the app always sends the most recent value and drops the stale ones in between. The slider stays smooth and never builds up a backlog of commands.

### Switching inputs (multi-computer setups)

If one monitor is cabled to two machines (say, a work laptop on USB-C and a personal PC on HDMI), open the panel and click the other input's button. The monitor switches instantly. To come back, use Brightness Tray on the other machine, or the monitor's own buttons.

The input list comes from the monitor's self-reported capabilities. If your monitor doesn't report one of its ports, or reports it wrongly, enable **Show all common inputs** in the tray menu to get the full standard list: VGA 1, DVI 1, DVI 2, DisplayPort 1, DisplayPort 2, HDMI 1, HDMI 2, HDMI 3, and USB-C.

### Tray menu

| Item | Effect |
| :--- | :--- |
| **Open brightness panel** (bold, default) | Opens the panel |
| **Refresh displays** | Re-detects all monitors |
| **Link all displays** ✓ | Same as the panel checkbox |
| **Show all common inputs** ✓ | Shows the full standard input list instead of what the monitor reports |
| **Scroll step** ▸ 1 % / 2 % / 5 % / 10 % / 20 % | Step used by the tray-icon wheel and the slider wheel |
| **Turn off displays** | Puts all displays to sleep after a short delay (any mouse or keyboard input wakes them) |
| **Start with Windows** ✓ | Adds or removes the sign-in startup entry |
| **Exit** | Closes the app |

### Launch behaviour

| How it was started | What happens |
| :--- | :--- |
| **Start menu / double-clicking the .exe** | Tray icon appears and the panel opens immediately |
| **Automatically at Windows sign-in** | Tray icon appears silently (no panel) |
| **Start menu while it's already running** | No second copy starts; the running copy opens its panel |

## UI design

The visual language follows Windows 11 Fluent design in dark mode, independent of the system app theme, so the panel always looks like a native system flyout.

### Panel mock-up

```
╭──────────────────────────────────────────────╮
│ Brightness                                   │
│                                              │
│ Built-in Display                    Built-in │
│ ━━━━━━━━━━━━━━━━━━━━━━━━●──────────       70 │
│ ──────────────────────────────────────────── │
│ DELL U2720Q                           DDC/CI │
│ ━━━━━━━━━━━━━━━●───────────────────       45 │
│ Input source                                 │
│ ( DisplayPort 1 )  [ HDMI 1 ]  ( USB-C )     │
│                                              │
├──────────────────────────────────────────────┤
│ ☑ Link all displays                ( Refresh )│
╰──────────────────────────────────────────────╯
                     [HDMI 1] = selected (accent colour)
```

### Visual specification

| Element | Design |
| :--- | :--- |
| **Window** | 380 px wide (DPI-scaled), height fits its content. Borderless, rounded corners on Windows 11, drop shadow, 1 px `#464646` hairline border, dark title-bar mode |
| **Background** | `#202020` |
| **Text** | Primary `#F0F0F0`; secondary/captions `#A0A0A0` |
| **Typography** | Segoe UI. Title 11.5 pt Semibold; display names and values 9.5 pt Semibold; body 9 pt; captions/tags 8.25 pt |
| **Separators** | 1 px `#3A3A3A` between displays and above the footer |
| **Slider track** | Rounded bar (≈ 1/7 of the control height, minimum 4 px). Unfilled `#5A5A5A`; filled portion in the Windows accent colour |
| **Slider thumb** | Fluent-style: a `#454545` outer disc with an accent-coloured inner dot that grows on hover (62 %), shrinks while dragging (45 %) and rests at 52 % |
| **Disabled slider** | Track only, no thumb, no accent (used for unsupported displays) |
| **Input / Refresh buttons** | Rounded "pill" buttons. `#373737` at rest, `#484848` on hover, accent fill when selected. The label switches to black if the accent colour is very light, so it stays readable |
| **Accent colour** | Read live from the user's Windows personalisation (`HKCU\Software\Microsoft\Windows\DWM\AccentColor`); falls back to Windows blue `#0078D7` |
| **Tray icon** | Drawn at runtime at the system small-icon size. White on a dark taskbar, near-black on a light taskbar. Redrawn automatically when the theme changes |
| **App icon** (Start menu, Explorer) | Amber sun (`#FFB81C`) on transparent, generated by `build.ps1` as a multi-resolution `.ico` (16–256 px) |
| **Placement** | 12 px from the screen's work-area edge on the monitor that holds the taskbar. Centred on the tray icon for a bottom taskbar; follows top or left taskbars |
| **DPI** | Every dimension is multiplied by the system DPI scale, so the panel looks right at 100 %, 125 %, 150 %, 200 %… |

## Technical architecture

### Technology choices

- **Language/runtime**: C# 5 on .NET Framework 4.x, which ships with every copy of Windows 10/11. The app is compiled by the `csc.exe` that comes with Windows, so building needs no Visual Studio or .NET SDK, and running it needs nothing to be installed.
- **UI**: Windows Forms with custom owner-drawn controls (slider, pill buttons) for the Fluent look.
- **Hardware access**: direct Win32 P/Invoke into `dxva2.dll` (DDC/CI), `user32.dll` (display enumeration, hooks), `shell32.dll` (tray icon geometry) and `dwmapi.dll` (window styling), plus WMI through `System.Management` for laptop panels.

### How brightness is controlled

Displays are driven in one of two ways, chosen automatically per display:

| Method | Used for | Mechanism |
| :--- | :--- | :--- |
| **DDC/CI** | External monitors | The Display Data Channel Command Interface is a small I²C side-channel inside the video cable. Windows exposes it via `dxva2.dll`. The app reads/writes VCP code `0x10` (luminance) with `GetVCPFeatureAndVCPFeatureReply` / `SetVCPFeature`. The monitor reports its own maximum (usually 100), and the app scales 0–100 % onto that range |
| **WMI** | Laptop / built-in panels | Internal panels don't speak DDC/CI; Windows controls their backlight through the `WmiMonitorBrightness` (read) and `WmiMonitorBrightnessMethods.WmiSetBrightness` (write) classes in `root\WMI` |
| **Unsupported** | Anything that answers neither | Shown greyed out with guidance text |

### Display discovery pipeline

`MonitorManager.Enumerate()` runs on a background thread and combines three Windows APIs, because none of them alone gives the full picture:

1. **Display paths**. `QueryDisplayConfig` lists the active video paths. For each, `DisplayConfigGetDeviceInfo` returns the Windows GDI source name (`\\.\DISPLAY1`), the monitor's EDID friendly name, its PnP device path, and the output technology. Output technology values of internal, LVDS, embedded DisplayPort or UDI embedded mark the display as the built-in panel.
2. **WMI instances**. All active `WmiMonitorBrightness` instances are read once.
3. **Physical monitors**. `EnumDisplayMonitors` walks every desktop monitor. `GetMonitorInfo` gives its GDI name and position, and `GetPhysicalMonitorsFromHMONITOR` yields the DDC/CI handle(s) behind it (more than one when screens are mirrored).
4. **Matching**. The device path (`\\?\DISPLAY#BOE0812#4&…#{guid}`) is converted to a PnP instance ID (`DISPLAY\BOE0812\4&…`) and matched against WMI instance names (`DISPLAY\BOE0812\4&…_0`). A match means WMI control. Otherwise a DDC/CI brightness read is attempted (up to 3 tries, 40 ms apart, since DDC is flaky). If that also fails and the display is internal, any unclaimed WMI instance is used as a last resort.
5. **Naming and ordering**. Names come from the EDID friendly name, else `Built-in Display`, else the driver description (ignoring "Generic PnP Monitor"), else `Display N`. Displays are sorted by their left edge to match the physical layout, and duplicate names get `(1)`, `(2)` suffixes.

### Input source switching

Input selection is MCCS VCP code `0x60`.
- **Discovering inputs**. After enumeration, each DDC/CI monitor's capabilities string is fetched in the background (`GetCapabilitiesStringLength` + `CapabilitiesRequestAndCapabilitiesReply`, up to 3 attempts, since this can take about a second). It looks like `(prot(monitor)type(LCD)…vcp(02 04 05 10 12 14(05 08 0B) 16 18 1A 60(0F 11 1B) …)…)`. A regular expression extracts the value list inside `60( … )`, giving the supported input codes.
- **Reading the current input** is a VCP `0x60` read (low byte).
- **Switching** is a VCP `0x60` write of the chosen code, done off the UI thread. The UI highlights the new input immediately, then confirms once the write completes.

**Which buttons are shown**:
- **Show all common inputs** on → the full standard list;
- else the inputs parsed from the capabilities string;
- else (no capabilities) a sensible default: DisplayPort 1, DisplayPort 2, HDMI 1, HDMI 2, USB-C;
- the currently active input is always added if missing.

**Input code names**:

| Code | Name | Code | Name | Code | Name |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `0x01` | VGA 1 | `0x07` | S-Video 1 | `0x0F` | DisplayPort 1 |
| `0x02` | VGA 2 | `0x08` | S-Video 2 | `0x10` | DisplayPort 2 |
| `0x03` | DVI 1 | `0x09`–`0x0B` | Tuner 1–3 | `0x11` | HDMI 1 |
| `0x04` | DVI 2 | `0x0C`–`0x0E` | Component 1–3 | `0x12` | HDMI 2 |
| `0x05` | Composite 1 | | | `0x13` | HDMI 3 |
| `0x06` | Composite 2 | | | `0x1B` | USB-C |

Unknown codes are shown as `Input 0xNN`.

### Threading and responsiveness

DDC/CI is slow: a single read or write commonly takes 40–100 ms, and a capabilities request can take a full second. The design keeps all of that off the UI thread:
- **UI thread** owns the tray icon, the flyout, the menu and the mouse hook. Work coming back from background threads is marshalled onto it with `Flyout.BeginInvoke` (the `Post` helper).
- **Thread pool** does enumeration, capability discovery, refresh reads, brightness writes and input writes.
- **Per-monitor I/O lock**. Every DDC/CI call for a given monitor goes through one lock, so a capabilities read, a brightness write and an input switch can never collide on the same I²C bus. Different monitors still run in parallel.
- **Write coalescing**. `SetBrightnessAsync` updates the cached value immediately (the UI is never blocked), stores it as pending, and starts at most one worker per monitor. The worker loops, always taking the newest pending value, until nothing is left. Dragging a slider across its whole range therefore produces only a handful of hardware writes, and the monitor ends on exactly the final value.
- **Safe disposal**. On re-enumeration the old `DisplayMonitor` objects are disposed on a background thread. Disposal takes the same I/O lock and marks the object dead, so in-flight writes on old handles finish or are skipped cleanly before `DestroyPhysicalMonitor` runs.

### Scroll-over-tray-icon implementation

Windows doesn't send mouse-wheel messages to notification-area icons, so the app watches the wheel globally, the same technique Twinkle Tray uses:
1. A low-level mouse hook (`SetWindowsHookEx(WH_MOUSE_LL)`) is installed on the UI thread at startup.
2. Once a second, the app asks the shell where its icon is (`Shell_NotifyIconGetRect`). The `NOTIFYICONIDENTIFIER` this needs (window handle and icon ID) is read by reflection from WinForms' `NotifyIcon` private `window` and `id` fields. Caching the rectangle keeps the hook callback fast, because Windows unhooks slow low-level hooks.
3. When a `WM_MOUSEWHEEL` arrives with the pointer inside that rectangle, the hook returns 1 to swallow the event and posts the wheel delta to the UI thread. There, deltas are accumulated in units of 120 (one notch) and each notch applies ± the scroll step.
4. With **Link all displays** on, every display is set to first display + step. Otherwise each display moves relative to its own level.

### Flyout window mechanics

- **Window styles**: `WS_EX_TOOLWINDOW` keeps it out of Alt+Tab, `CS_DROPSHADOW` adds a shadow, and `TopMost` keeps it above other windows.
- **DWM attributes**: `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND` for Windows 11 rounded corners, and `DWMWA_USE_IMMERSIVE_DARK_MODE`.
- **Close-on-blur**: the form hides itself on `Deactivate`. Clicking the tray icon while the panel is open first deactivates (and hides) it, so the click handler ignores clicks that arrive within 300 ms of a hide. Otherwise the panel would immediately reopen.
- **Foreground activation**: `Show()` + `Activate()` + `SetForegroundWindow()` so keyboard input and close-on-blur work reliably.
- **Rebuild vs. sync**: `Build()` lays out every control from scratch (used when the monitor list or input lists change). `SyncValues()` only pushes cached values into existing sliders and buttons, skipping any slider the user is currently dragging.

### System integration

| Event | Reaction |
| :--- | :--- |
| `SystemEvents.DisplaySettingsChanged` (monitor plugged/unplugged, resolution or arrangement change) | Re-enumerate after a 2 s debounce, so a burst of changes triggers one reload |
| `SystemEvents.PowerModeChanged` → `Resume` | Same debounced re-enumerate (DDC handles often go stale across sleep) |
| `SystemEvents.UserPreferenceChanged` (theme/colour) | Redraw the tray icon for the new taskbar colour |
| **Second launch** | Program fails to take the mutex `Local\BrightnessTray.SingleInstance`, signals the named event `Local\BrightnessTray.Show`, and exits. The running copy, waiting on that event via `ThreadPool.RegisterWaitForSingleObject`, opens its panel |
| **Sign-in** | `HKCU\…\CurrentVersion\Run\BrightnessTray` = `"…\BrightnessTray.exe" --startup`. The `--startup` flag suppresses the panel on launch. Older entries without the flag are rewritten automatically on start |
| **Turn off displays** | After a 500 ms delay (so the click itself doesn't wake the screens), posts `WM_SYSCOMMAND` / `SC_MONITORPOWER` = 2 |
| **Unhandled exceptions** | Caught at the application level and appended to `error.log` instead of crashing the tray app |
| **DPI awareness** | `app.manifest` declares the process system-DPI-aware. The flyout computes its scale factor from the screen DPI at startup and multiplies every hard-coded dimension by it (`S()` helper), while fonts are sized in points and scale naturally. The tray icon is drawn at `SystemInformation.SmallIconSize`. |

## Source code map

```
BrightnessTray/
├── src/
│   ├── Program.cs           Entry point: single-instance mutex, "show panel" event, --startup flag, crash logging
│   ├── TrayApp.cs           ApplicationContext: tray icon + menu, mouse-wheel hook, tray-rect tracking,
│   │                        monitor (re)loading, linked/unlinked logic, system event handling
│   ├── Flyout.cs            The popup panel: layout, positioning near the tray, close-on-blur, value syncing
│   ├── Controls.cs          Theme colours/accent/taskbar detection, rounded-rect helper, runtime sun icon,
│   │                        BrightnessSlider and PillButton owner-drawn controls
│   ├── Monitors.cs          DisplayMonitor (DDC/CI + WMI I/O, coalesced writes, capabilities parsing),
│   │                        InputSource names, WmiBrightness helper, MonitorManager enumeration and matching
│   ├── Settings.cs          settings.ini load/save and the Start-with-Windows registry value
│   └── Native.cs            All P/Invoke declarations (user32, dxva2, shell32, kernel32, dwmapi)
├── assets/
│   └── BrightnessTray.ico   Multi-size app icon (generated by build.ps1 if missing)
├── app.manifest             DPI awareness, Windows 10/11 compatibility, asInvoker
├── build.ps1                Generates the icon (first run) and compiles bin\BrightnessTray.exe with csc.exe
├── install-shortcut.ps1     Creates the Start menu shortcut
└── diagnose.ps1             Prints what the app detects for each display (control method, inputs, …)
```

## Building

`build.ps1`:
1. Locates `csc.exe` under `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319` (falls back to the 32-bit Framework folder).
2. If `assets\BrightnessTray.ico` doesn't exist, draws the amber sun with `System.Drawing` and writes a multi-resolution `.ico`: classic 32-bit DIB frames at 16, 20, 24, 32, 40, 48 and 64 px plus a PNG-compressed 256 px frame.
3. Stops any running `BrightnessTray.exe` (so the file isn't locked).
4. Compiles `src\*.cs` as a winexe with the manifest and icon embedded, referencing `System`, `System.Core`, `System.Drawing`, `System.Windows.Forms` and `System.Management`.

Because the compiler is the in-box .NET Framework one, the source sticks to **C# 5 syntax** (no string interpolation, `?.`, expression-bodied members, etc.).

After moving the project folder, re-run `.\install-shortcut.ps1` so the Start menu shortcut points at the new location.

## Configuration and files on disk

| Location | Contents |
| :--- | :--- |
| `%APPDATA%\BrightnessTray\settings.ini` | `Linked=0/1`, `ShowAllInputs=0/1`, `ScrollStep=1–25` |
| `%APPDATA%\BrightnessTray\error.log` | Any unexpected exceptions (only created if something goes wrong) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BrightnessTray` | Present only when Start with Windows is on |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Brightness Tray.lnk` | Start menu shortcut |

No administrator rights are needed for anything. To uninstall: untick Start with Windows, exit the app, delete the shortcut and the `%APPDATA%\BrightnessTray` folder, then delete the project folder.

## Troubleshooting

- **An external monitor shows "Not supported"**: Open the monitor's on-screen menu and make sure DDC/CI is enabled (it's often under System, Others or Setup). Docks, KVMs and especially DisplayLink USB adapters often don't pass DDC/CI through. Try connecting the monitor directly to the laptop's HDMI/DisplayPort/USB-C port. Click Refresh after changing anything.
- **The input I need isn't listed**: Turn on **Show all common inputs** in the tray menu.
- **Clicking an input does nothing**: A few monitors only accept input changes for ports that currently have a signal, or ignore DDC/CI while in power-saving mode. Make sure the other computer is awake and outputting video.
- **Scrolling over the tray icon does nothing**: Drag the icon out of the hidden-icons overflow onto the taskbar.
- **See exactly what's detected**: Close the app (tray menu → Exit) and run `.\diagnose.ps1`. Each display is printed with its control method, brightness, current input code and the input codes from its capabilities string.

## Known limitations

- Monitors that are mirrored onto one desktop are matched to their DDC/CI handles by order, which Windows usually (but doesn't strictly guarantee to) keep consistent.
- Input codes are sent as plain MCCS values. A few vendor-specific monitors use non-standard codes in the high byte and won't switch with the standard list.
- The process is system-DPI-aware rather than per-monitor-aware. The panel always opens on the taskbar's monitor, so this rarely matters, but with mixed-DPI setups it may be slightly scaled.
- Locating the tray icon uses reflection on .NET Framework's `NotifyIcon` internals. If that ever fails, everything still works except scroll-over-icon.
- Not (yet) included from Twinkle Tray: global hotkeys, time-of-day brightness schedules, per-monitor brightness offsets when linked, contrast/volume controls, and per-monitor rename/hide.
