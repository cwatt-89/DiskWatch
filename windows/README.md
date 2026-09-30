# DiskWatch for Windows

C# / .NET 10 with Avalonia UI. The same design system and features as the macOS app.

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

- `app.manifest` requests `requireAdministrator`, so Windows shows the UAC prompt on every launch.
- Deleting uses the Recycle Bin (`SHFileOperation` with undo); if an item is too large for the bin, Windows asks before deleting it outright.
- Restore points (volume shadow copies) are listed in Cleanup via `vssadmin`.
- "Size on Disk" rounds each file up to the volume's cluster size; cloud-only OneDrive files count as 0 on disk.
  Hard links are counted once per path (Windows doesn't expose link identity cheaply).
- Design tokens and components: `UI/FN.cs`; list/menu/text-box styling: `App.axaml`.
- Runs on macOS for development: `DISKWATCH_NO_ELEVATE=1 dotnet run`.
