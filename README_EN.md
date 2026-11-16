# PinWins

Pin any window on your Windows desktop to the top — a tiny, tray-resident always-on-top utility.

[中文文档](README.md)

---

## Overview

PinWins is a small Windows desktop tool built with .NET 10 and Avalonia. Point your mouse at any window, press your own global hotkey, and that window is pinned on top with a bright colored border around it, so you can spot it instantly while multitasking.

It lives in the system tray (no taskbar button), and closing the settings window does not exit the app.

## Features

- **Pin / unpin with one hotkey**: hover a target window and press the global hotkey — no need to activate it first.
- **Colorful border indicator**: pinned windows get an overlay outline, and on Windows 11 the native window border color is changed as well.
- **Multiple pinned windows**: enable multi-window mode and each pinned window is assigned a distinct color.
- **Custom appearance**: pick any border color and a width from 1 to 10 px (default 6 px).
- **Custom global hotkey**: any combination of Ctrl / Alt / Shift / Win, recorded by simply pressing keys in the settings window.
- **Pinned windows preview panel**: open it from the tray menu to see a live thumbnail and title of every pinned window:
  - thumbnails refresh automatically, no need to reopen the panel;
  - click a card to bring that window to the front;
  - the window that currently has the foreground is highlighted with a **red border**;
  - the close button in the card corner unpins that window directly.
- **Tray resident**: the tray icon is restored automatically after Explorer restarts; left click opens settings, right click opens the menu.
- **Single instance**: launching a second copy brings the existing instance's settings window to the front instead of starting another process.
- **Diagnosable**: key steps are logged to files for troubleshooting.

## Downloads

Two equivalent builds are provided; pick either one:

| Build | Description | When to use |
| --- | --- | --- |
| **Single file** (`PinWins-1.0.0-single`) | Just one `PinWins.exe`, compressed; extracts itself to a temp folder on first launch | Copy a single file around, or keep it on a USB stick |
| **Portable folder** (`PinWins-1.0.0-portable`) | A folder with the exe and all dependencies | Faster startup, copy the whole folder, no installation |

Both are **self-contained** builds and **do not** require a preinstalled .NET runtime — unzip and run.

## Quick start

1. Download and extract the build you prefer (for the single-file build, just run the exe).
2. On first launch the settings window opens. Click the "Global hotkey" area, press the combination you want (e.g. `Ctrl + Alt + T`), then click "Save".
3. Hover any window and press the hotkey — the window is pinned and gets a colored border.
4. Press the hotkey again (or use the close button in the preview panel) to unpin it.

## Settings

| Option | Description |
| --- | --- |
| Global hotkey | Toggles the pin state of the window under the mouse. Must include at least one modifier: Ctrl / Alt / Shift / Win. |
| Border color | Outline color for pinned windows, default `#FF3B30` (red). |
| Border width | Outline width, 1–10 px, default 6 px. Larger is more visible but may cover window edges. |
| Allow multiple pinned windows | Off by default: pinning a new window unpins the previous one. On: every pinned window gets its own color. |

## Tray menu

Right-click the tray icon:

- **Settings (S)…** — open the settings window
- **Unpin all (U)** — remove every pinned window at once
- **Pinned windows (P)…** — open the preview panel (thumbnails / activate / unpin)
- **Exit (X)** — quit and release all pins

> A left click on the tray icon is the same as **Settings**.

## Data and log locations

Everything is stored in the current user's AppData folder; delete that folder to clean up completely:

| Content | Path |
| --- | --- |
| Settings | `%APPDATA%\PinWins\settings.json` |
| Runtime log | `%APPDATA%\PinWins\app_yyyyMMdd.log` |
| Log fallback | `%TEMP%\PinWins\app_yyyyMMdd.log` |
| Crash log | `%APPDATA%\PinWins\crash_yyyyMMdd.log` |

## Requirements

- Windows 10 / Windows 11 (64-bit)
- No .NET runtime installation required (self-contained builds)
- Native colored borders require Windows 11; on Windows 10 the overlay outline is still drawn

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/):

```powershell
# Run in debug
dotnet run

# Release build
dotnet build -c Release

# Publish: single file
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o dist\PinWins-1.0.0-single

# Publish: portable folder
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:DebugType=None -p:DebugSymbols=false -o dist\PinWins-1.0.0-portable
```

> A self-contained publish also brings in the SkiaSharp / HarfBuzzSharp `.pdb` symbols (large files); you can delete them.

## Project structure

```
PinWins/
├─ Program.cs                 Entry point: single-instance mutex, crash log, Avalonia startup
├─ App.axaml(.cs)             Avalonia application and lifetime
├─ Core/
│  ├─ AppController.cs        Main controller: settings, hotkey, tray, preview panel
│  ├─ PinManager.cs           Pin state, color assignment, overlay maintenance
│  ├─ WindowHelper.cs         Window enumeration / hit test / activate / topmost
│  ├─ BorderOverlay.cs        Overlay window that draws the outline
│  ├─ Logger.cs               Lightweight file logger
│  └─ ColorUtil.cs            Color conversions
├─ Interop/                   Win32 / DWM / Shell interop and thumbnail capture
├─ Services/
│  ├─ GlobalHotkeyService.cs  Global hotkey registration
│  ├─ TrayIconService.cs      Shell_NotifyIcon tray icon
│  └─ SettingsService.cs      JSON settings persistence
├─ Models/                    Settings and hotkey models
└─ Views/                     Settings window and pinned windows preview panel
```

## Known limitations

- Some fullscreen games, UWP / Store apps, and custom-drawn windows cannot be pinned or outlined.
- Windows running elevated require this tool to run with the same privileges.
- If a hotkey is already taken by another program, choose a different one in settings.
- Only a 64-bit (win-x64) build is provided.

## License

[MIT](LICENSE)
