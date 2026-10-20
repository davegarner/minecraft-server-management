# Release Notes

## 1.0.0 (20 October 2026)

First release.

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
