# DiskWatch for macOS

SwiftUI + AppKit, built with Swift Package Manager — no Xcode project needed.

```bash
./scripts/build.sh            # builds build/DiskWatch.app (ad-hoc signed)
./scripts/build.sh --install  # …and copies it to /Applications
```

- On every launch DiskWatch asks for an administrator password, then relaunches itself (detached, in its own session) as root.
- Grant **Full Disk Access** so root can read Mail, Messages, Safari and app containers. The app is ad-hoc signed,
  so after a rebuild you may need to toggle it off and on again.
- Design tokens and components: `Sources/DiskWatch/Theme.swift`.
