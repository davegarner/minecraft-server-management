# Release Notes

## 1.0.6 (24 October 2026)

A small feature update. Run the new setup over the previous version; your settings, worlds and backups are kept.

- **Light and dark theme button:** the **◐** button at the top right, next to **Sign out**, switches the panel between light and dark whatever your device is set to. Your browser remembers the choice; switch back to your device's setting and the panel follows the device again.

## 1.0.5 (24 October 2026)

A bug fix for newer Minecraft servers. Run the new setup over the previous version; your settings, worlds and backups are kept.

- **Online players:** the Dashboard's player count and list update again when players join and leave. Newer Minecraft servers (such as 26.3) log these events as "System chat: Steve joined the game", which the panel didn't recognise, so it always showed 0 players online. Older Minecraft versions still work as before.

## 1.0.4 (21 October 2026)

A security update; upgrading is recommended. (Version 1.0.3 was tagged but never released; 1.0.4 contains its changes.) Run the new setup over the previous version; your settings, worlds and backups are kept.

- **The service no longer runs as Local System.** It runs as its own virtual account, `NT SERVICE\MinecraftServerManager`, and the installer limits the panel's settings, sign-in keys and server folders to administrators and that account, so other users on the server can't read them or change the server's files. **Java must be installed system-wide**: a JDK inside a user's profile can't be used by the service ([details](Running-as-a-Windows-Service#service-account)).
- **Sign-in keys** are encrypted with Windows DPAPI.
- **Changing the password signs out open panel tabs** within 30 seconds; before, they stayed signed in.
- **First-run setup only works on the server itself** (`http://localhost:<port>`), which also stops a web page taking it over through DNS rebinding. Set `Manager:AllowRemoteSetup` to allow it from another computer.
- **Archives that would fill the disk are refused** when importing a world or restoring a backup, and a `level.dat` that expands past 32 MB is ignored.
- **Brute-force protection** counts IPv6 addresses per /64 network.

## 1.0.2 (21 October 2026)

A maintenance release. To upgrade, run the new setup over the previous version; your settings, worlds and backups are kept.

- **Under the hood:** the code is reorganised by feature with enforced dependency rules, and player tracking, server log parsing and the crash-restart rules are now covered by tests. Features and behaviour are unchanged.
- **Players:** if saving the whitelist setting fails, the page now reports the error instead of disconnecting.

## 1.0.1 (21 October 2026)

A performance update. To upgrade, run the new setup over 1.0.0; your settings, worlds and backups are kept.

- **Console:** much less work while output is streaming in (about 8× less server time per update, with almost no memory churn), so bursts such as world generation stay smooth even with several Console tabs open.
- **Dashboard:** reads only the active world instead of every world, so it opens and refreshes faster on servers with several large worlds. Online players and the console preview are cheaper to refresh.
- **Worlds and backups:** deleting a large world, and cleaning up after a restore, make far fewer file-system calls.

## 1.0.0 (21 October 2026)

First release. Download it from the [Releases page](https://gitea.uk.local:3001/Apps/minecraft-server-management/releases).

### Installation

- **Setup wizard** (`MinecraftServerManager-1.0.0-Setup.exe`): installs to Program Files, registers and starts the Windows service, adds the firewall rules you choose, creates a Start menu shortcut and opens the panel. Running a newer version upgrades in place and keeps your settings.
- **MSI** (`MinecraftServerManager-1.0.0-x64.msi`) for unattended installs, Group Policy and Intune, with `PANELPORT`, `REMOTEACCESS` and `OPENMINECRAFTPORT` properties.
- **Portable zip** with `install-service.ps1` for manual installs.
- Panel settings are kept in `C:\ProgramData\Minecraft Server Manager`, so upgrades never overwrite them. `MinecraftServerManager.exe configure` changes the panel's port and network access.

### Features

- **Server control:** start, stop (graceful, with timeout), restart and force-kill; live status, uptime, CPU, memory and online players; automatic restart after crashes (up to 3 in 10 minutes); clean shutdown with Windows.
- **Console:** live colour-coded output, filtering, command input with history, quick commands.
- **Server Properties editor:** all vanilla settings with typed inputs, descriptions, categories, search and validation; preserves comments and order; correct Java escaping.
- **Worlds:** generate worlds (seed or random, Default / Large Biomes / Amplified / Superflat with presets / Single Biome, game mode, difficulty, hardcore, structures); switch the active world; import zipped worlds (up to 8 GB); delete with backup; shows version, seed, game mode, size and last played from `level.dat`.
- **Backups:** safe live backups (`save-off` / `save-all flush` / `save-on`); scheduled backups with retention; restore with safety backup and automatic rollback; download; free-space checks.
- **Updates:** install any release, snapshot or legacy version from Mojang; SHA-1 verification; Java requirement check; automatic pre-update backup and restart; rollback to the last five jars; update notifications.
- **Players:** operators, whitelist (with on/off), banned players and IPs; online player actions (op, kick, ban); works with the server running or stopped.
- **Settings:** server and backup folders, Java detection and testing, memory presets, recommended G1GC flags, launch preview, auto-start, crash restart, stop timeout, password change.
- **Platform:** single self-contained `.exe`; Windows service with install/uninstall scripts and firewall options; Event Log logging; works on phones and tablets; light and dark appearance.
- **Security:** single admin account with PBKDF2 hashing, sign-in lockout, anti-forgery protection, session invalidation on password change, path-traversal and zip-slip protection.

### Known limitations

- One Minecraft server per panel; one admin account.
- Updates install the vanilla server only (other jars can be launched but must be updated by hand).
- Java Edition only (no Bedrock).
- Behind a reverse proxy, the sign-in lockout counts attempts per proxy address.
- The installers aren't code-signed, so SmartScreen may warn when you first run them.
