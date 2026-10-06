## Download

| Platform | File |
|---|---|
| Windows 10/11 (Intel/AMD) | `Qtoxide-@VERSION@-win-x64.zip` |
| Windows 11 on ARM | `Qtoxide-@VERSION@-win-arm64.zip` |
| macOS 12+ Apple Silicon (M1–M4) | `Qtoxide-@VERSION@-osx-arm64.zip` |
| macOS 12+ Intel | `Qtoxide-@VERSION@-osx-x64.zip` |
| Linux (x86-64) | `Qtoxide-@VERSION@-linux-x64.tar.gz` |
| Linux (ARM64, e.g. Raspberry Pi 4/5 64-bit) | `Qtoxide-@VERSION@-linux-arm64.tar.gz` |

Nothing else to install: .NET is included.

### First start

- **Windows**: extract the zip and run `Qtoxide.exe`. SmartScreen may warn about an unknown
  publisher (the app is not code-signed): choose *More info → Run anyway*.
- **macOS**: extract the zip and move `Qtoxide.app` to *Applications*. The app is not notarized by
  Apple, so the first time macOS refuses to open it: open *System Settings → Privacy & Security* and
  click *Open Anyway*, or run `xattr -dr com.apple.quarantine /Applications/Qtoxide.app` in Terminal.
- **Linux**: `tar xzf Qtoxide-@VERSION@-linux-x64.tar.gz`, then run `./Qtoxide` from the folder, or
  `./install.sh` to add it to your applications menu. Needs an X11 or XWayland desktop.

Profiles are compatible with qTox: use *Import…* on the login screen to bring yours over.
`SHA256SUMS.txt` lists the checksums of every file.
