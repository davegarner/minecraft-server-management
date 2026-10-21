A web control panel for running a **Minecraft: Java Edition** dedicated server on **Windows Server**. Manage it from your browser: start and stop the server, edit settings, generate worlds, take backups and install updates.

## Download

| File | Use it if… |
| --- | --- |
| **MinecraftServerManager-{{VERSION}}-Setup.exe** | You want the simplest install. A short wizard installs everything and opens the panel. **Start here.** |
| MinecraftServerManager-{{VERSION}}-x64.msi | You deploy software with Group Policy, Intune or another tool, or want an unattended install. |
| MinecraftServerManager-{{VERSION}}-win-x64.zip | You'd rather install by hand (includes `install-service.ps1`). |
| SHA256SUMS.txt | Checksums to verify the downloads. |

**Requirements:** Windows Server 2016 or later, or Windows 10/11 (64-bit), plus Java for the Minecraft version you run (Java 21 for 1.20.5 – 1.21.x, Java 25 for 26.x). No .NET installation is needed.

## Install

1. Download and run **MinecraftServerManager-{{VERSION}}-Setup.exe**.
2. Choose the panel port (default 8080) and which firewall rules to add, then click **Install**.
3. Click **Finish**. The panel opens in your browser at `http://localhost:8080`. Create your admin account, select Java in **Settings**, and install Minecraft from **Updates**.

The panel runs as a Windows service called *Minecraft Server Manager*, so it keeps running when you sign out and starts with Windows. Panel settings are kept in `C:\ProgramData\Minecraft Server Manager`; your worlds go in `C:\MinecraftServer` by default.

**Upgrading:** run the new setup over the old one. The service is stopped, updated and restarted, and your settings, worlds and backups are kept.

**Unattended install:**

```powershell
MinecraftServerManager-{{VERSION}}-Setup.exe /VERYSILENT /PORT=8080 /REMOTEACCESS=0 /OPENMINECRAFTPORT=1
msiexec /i MinecraftServerManager-{{VERSION}}-x64.msi /qn PANELPORT=8080 REMOTEACCESS=0 OPENMINECRAFTPORT=1
```

> **"Windows protected your PC"?** The installers aren't code-signed yet, so SmartScreen may warn about them. Check the file's SHA-256 against `SHA256SUMS.txt`, then click **More info → Run anyway**.

## Documentation

Full user guide, administration and troubleshooting: **[project wiki](https://github.com/davegarner/minecraft-server-management/wiki)**. The user guide is also available as a [Word document](https://github.com/davegarner/minecraft-server-management/raw/main/docs/Minecraft-Server-Manager-User-Guide.docx).

## What's included

- **Server control:** start, stop, restart, live status, CPU and memory, online players, automatic restart after a crash, clean shutdown with Windows.
- **Console:** live colour-coded output and commands.
- **Server Properties:** every setting with descriptions and validation.
- **Worlds:** generate (seeds, world types, superflat presets), switch, import and delete.
- **Backups:** safe backups of a running world, schedules with retention, one-click restore.
- **Updates:** install or update the official Minecraft server, verified, with automatic backup and rollback.
- **Players:** operators, whitelist and bans.

See the [release notes](https://github.com/davegarner/minecraft-server-management/wiki/Release-Notes) for details and known limitations.
