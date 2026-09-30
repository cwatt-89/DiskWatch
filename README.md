# DiskWatch.

A usage analyzer and cleaner, for **macOS** and **Windows**, built to the
FloydNet Terminal design system: black ground, hairline compartments, zero radius, one green accent,
stretched Times New Roman over monospace.

| | macOS | Windows |
|---|---|---|
| Source | [`macos/`](macos) — Swift, SwiftUI + AppKit | [`windows/`](windows) — C#, .NET 10 + Avalonia |
| Admin rights | Asks for your password every launch, relaunches as root | UAC prompt every launch (`requireAdministrator` manifest) |
| Download | [`DiskWatch-macos.zip`](https://github.com/cwatt-89/DiskWatch/releases/latest) | [`DiskWatch.exe`](https://github.com/cwatt-89/DiskWatch/releases/latest) |

## Install

Download from the [latest release](https://github.com/cwatt-89/DiskWatch/releases/latest).

### Windows 10 / 11

1. Download **`DiskWatch.exe`** and put it anywhere (e.g. a `DiskWatch` folder in Documents). There's no installer — the exe is the whole app.
2. Double-click it and choose **Yes** at the administrator (UAC) prompt. It asks every launch.
3. First launch only: if SmartScreen says *"Windows protected your PC"*, click **More info → Run anyway** (the app isn't code-signed).
4. Optional: right-click the exe → **Pin to Start** / **Pin to taskbar**.

### macOS 14 or later

1. Download **`DiskWatch-macos.zip`**, double-click to unzip, and drag **DiskWatch.app** into **Applications**.
2. Open it. The first time, macOS blocks it because it isn't signed with an Apple Developer ID: click **Done**, open
   **System Settings → Privacy & Security**, scroll down and click **Open Anyway**, then confirm.
3. Enter your Mac password at DiskWatch's administrator prompt. It asks every launch.
4. Recommended: **System Settings → Privacy & Security → Full Disk Access** → turn on **DiskWatch**, then reopen it.
   Without this, Mail, Messages, Safari and some app data can't be measured or cleaned.

## Features (both platforms)

- **Overview** — capacity gauge with a 90% ceiling marker, scan stats, biggest items, space by kind, largest and stale files.
- **Tree** — expandable folders with size, share-of-parent bars, file/folder counts, dates and a safety badge; every column sorts.
- **Treemap** — squarified treemap; hover for details, double-click to zoom, right-click for actions.
- **Files** — every file, filterable by name, kind, minimum size and age.
- **Types** — usage by kind and by extension.
- **Cleanup** — known caches, logs, temp files, developer caches, old installers, the Trash/Recycle Bin and local snapshots/restore points, with sizes.
- **Duplicates** — byte-for-byte identical files (SHA-256); never lets you mark every copy.
- Inspector, search, CSV export, keyboard shortcuts, per-folder rescan, excluded folders.

## Safety

Every item is classified before anything can be removed:

| Level | Examples | Deleting |
|---|---|---|
| Safe | caches, logs, temp files, Trash/Recycle Bin, Downloads | normal confirmation |
| Review first | your own files, app data, installed apps | normal confirmation |
| Dangerous | system-wide app data, cloud-synced folders, device backups, SSH keys, Windows.old | must type `DELETE` |
| Protected | macOS: SIP, `/System`, `/usr`, keychains · Windows: `C:\Windows`, pagefile/hiberfil, System Volume Information, WindowsApps, registry hives | never |

Deleting defaults to the Trash / Recycle Bin; permanent deletion needs an extra acknowledgment.
Every action is logged (`~/Library/Logs/DiskWatch/deletions.log` on macOS,
`%LOCALAPPDATA%\DiskWatch\Logs\deletions.log` on Windows).

## Building

**macOS** (Command Line Tools are enough):

```bash
cd macos && ./scripts/build.sh --install
```

**Windows** (on Windows, or cross-compiled from macOS/Linux with the .NET 10 SDK):

```bash
cd windows && dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

The Windows app also runs on macOS for development: `DISKWATCH_NO_ELEVATE=1 dotnet run` in `windows/`.

## Notes

- Neither build is code-signed. macOS: right-click → Open the first time, and grant **Full Disk Access**
  (System Settings → Privacy & Security). Windows: SmartScreen may warn — *More info → Run anyway*.
- Debug helpers on both: `--selftest [path]` (headless scan + safety check), `DISKWATCH_NO_ELEVATE=1`,
  `DISKWATCH_SNAPSHOT=<dir>` (renders every screen to PNG).
