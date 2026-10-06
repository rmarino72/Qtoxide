# Qtoxide

A desktop [Tox](https://tox.chat) messenger in the spirit of qTox, written in C# with
[Avalonia](https://avaloniaui.net) on top of [Toxide](https://www.nuget.org/packages/Toxide), a managed implementation of the
Tox protocol. Runs on Windows, macOS and Linux and talks to qTox, uTox, Toxic and every other
toxcore-based client.

![Qtoxide chat (dark theme)](docs/screenshots/chat-Dark.png)

<p align="center">
  <img src="docs/screenshots/login-Light.png" width="32%" alt="Login" />
  <img src="docs/screenshots/chat-Light.png" width="32%" alt="Chat (light theme)" />
  <img src="docs/screenshots/settings-Light.png" width="32%" alt="Settings" />
</p>

## Download

Get the latest version from the **[Releases page](https://github.com/rmarino72/Qtoxide/releases/latest)**:

| Platform | Download |
|---|---|
| Windows 10/11 | `win-x64.zip` (ARM: `win-arm64.zip`) |
| macOS 12+ | `osx-arm64.zip` for Apple Silicon, `osx-x64.zip` for Intel |
| Linux | `linux-x64.tar.gz` (ARM64: `linux-arm64.tar.gz`) |

Each package is self-contained: no .NET installation needed. The builds are not code-signed by
Microsoft or notarized by Apple, so the first start needs one extra click: on Windows
*More info → Run anyway*; on macOS *System Settings → Privacy & Security → Open Anyway*. On Linux,
run `./install.sh` from the extracted folder to add Qtoxide to the applications menu.

## Features

- **Profiles**: create several, protect them with a password (profile *and* chat history are
  encrypted), import `.tox` files from qTox and other clients, export them back.
- **Friends**: add by Tox ID (also `tox:` links), accept or decline requests, presence
  (online / away / busy), status messages, typing indicators, unread counters.
- **Chat**: persistent history, read receipts (✓ sent, ✓✓ delivered), `/me` actions, long messages
  split automatically, messages to offline friends queued and delivered when they come back.
- **Files**: send and receive with progress, pause/resume and cancel; avatars exchanged like qTox
  (PNG, at most 64 KiB, deduplicated by hash).
- **Settings**: name, status, avatar, Tox ID with QR code, new nospam, password change, desktop
  notifications, download folder, LAN discovery, IPv6, extra bootstrap nodes.

Not yet: audio/video calls, group chats, TCP relays (friends must be reachable over UDP,
possibly after NAT hole punching) — these depend on Toxide.

## Build and run

Requires the .NET 8 SDK. Toxide comes from NuGet ([Toxide](https://www.nuget.org/packages/Toxide)).

```bash
dotnet run --project src/Qtoxide
```

Data lives in `%LOCALAPPDATA%\Qtoxide`, `~/Library/Application Support/Qtoxide` or
`~/.local/share/Qtoxide` (set `QTOXIDE_HOME` to use another folder, e.g. a portable install).
Each profile is a qTox-compatible `profiles/<name>.tox` file, with `<name>.history`,
`<name>.settings.json` and `<name>.avatars/` next to it.

## Packaging

`build/package.sh <version> <rid>` builds one downloadable package into `dist/` (see the script for
the runtimes). Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`, which tests, packages every
platform, starts each package on a real Windows, Linux and macOS machine, and publishes the release.

## Tests

```bash
dotnet test
```

- service tests (profiles, passwords, history encryption, imports, message splitting);
- an end-to-end test: two clients and relay nodes over real UDP on localhost, going through friend
  request, chat, receipts, `/me`, file transfer, avatar and offline message delivery;
- headless rendering of every screen in light and dark theme, saved to `screenshots/` (the images in
  `docs/screenshots/` come from there).

## License

GPL-3.0-or-later, like Toxide and toxcore.
