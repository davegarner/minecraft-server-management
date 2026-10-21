# Minecraft Server Manager

A web-based control panel for running a **Minecraft: Java Edition** dedicated server on **Windows Server**.
It ships as a single self-contained `.exe` (no .NET install needed), runs as a Windows service, and is
managed from a browser, either on the server itself or remotely.

## Documentation

- **[Project wiki](https://gitea.uk.local:3001/Apps/minecraft-server-management/wiki)**: user guide with screenshots, installation, administration, troubleshooting and developer reference (source in [`docs/wiki`](docs/wiki)).
- **[User Guide (Word)](docs/Minecraft-Server-Manager-User-Guide.docx)**: the same user guide as a printable document.

## Features

| Area | What you can do |
| --- | --- |
| **Dashboard** | Start / stop / restart / force-kill the server, live status, uptime, CPU and memory, online players, update notices, recent console output |
| **Console** | Live, colour-coded server log; send commands (with ↑/↓ history); quick commands; filter output |
| **Server Properties** | Edit every `server.properties` key with typed inputs (toggles, dropdowns, validated numbers), descriptions, categories and search. Comments and key order are preserved, and Java escaping is handled correctly |
| **Worlds** | Generate new worlds (name, seed or random seed, Default / Large Biomes / Amplified / Superflat with presets / Single Biome, game mode, difficulty, hardcore, structures), switch the active world, import a zipped world (e.g. a singleplayer save), delete worlds. Shows each world's version, seed, game mode and size read from `level.dat` |
| **Backups** | One-click backups of any world. A running world is backed up safely: autosave is paused (`save-off`), everything is flushed to disk (`save-all flush`), the world is zipped, then autosave resumes. Scheduled backups with retention, restore (with an automatic safety backup and rollback on failure), download and delete |
| **Updates** | Install or update the official server jar from Mojang's version manifest (releases, snapshots, legacy). Downloads are SHA-1 verified, the Java requirement is checked first, the world is backed up, and the server is stopped, updated and restarted. Previous jars are kept for one-click rollback |
| **Players** | Operators, whitelist (and whitelist on/off), banned players and banned IPs. Uses console commands while the server runs, or edits the JSON files directly (with Mojang UUID lookup) while it's stopped. Kick, op or ban online players |
| **Settings** | Server directory, Java detection and testing, memory (with presets) and JVM flags (recommended G1GC preset), auto-start, crash auto-restart, stop timeout, admin password |

It also handles:

- **Crash recovery**: if the server crashes it is restarted automatically, up to 3 times in 10 minutes.
- **Clean shutdown**: stopping the service or shutting down Windows sends `stop` to Minecraft and waits for the world to save.
- **Orphan protection**: the Java process is tied to the manager through a Windows job object, so it never keeps running without a way to control it.
- **Security**: a single admin account (PBKDF2-hashed password), login lockout after repeated failures, anti-forgery protection, and sign-in cookies that are invalidated when the password changes.

## Requirements

- Windows Server 2016 or newer, or Windows 10/11 (x64).
- **Java** for the Minecraft version you run. Recent versions need Java 21 or 25 (the Updates page shows the exact requirement for each version). [Eclipse Temurin](https://adoptium.net/temurin/releases/) is recommended.
- Internet access from the server to download Minecraft and check for updates.

## Quick start

1. **Get the release.** Copy `MinecraftServerManager-win-x64.zip` (see [Building](#building)) to the server and extract it to a permanent folder, for example `C:\MinecraftManager`.
2. **Run it.** Double-click `MinecraftServerManager.exe`, or install it as a service (below).
3. **Open the panel** at <http://localhost:8080> in a browser on the server.
4. **Create the admin account** and choose the server directory (default `C:\MinecraftServer`).
5. Go to **Settings → Java**, click **Detect installed Java**, pick the right Java and **Save**.
6. Go to **Updates** and install the latest release. Accept the Minecraft EULA when prompted.
7. Optionally adjust **Server Properties** or create a world on **Worlds**, then press **Start** on the Dashboard.

### Run as a Windows service (recommended)

From an **elevated** PowerShell prompt in the extracted folder:

```powershell
.\install-service.ps1 -OpenMinecraftPort
```

This registers the `MinecraftServerManager` service (automatic start, restart on failure) and opens TCP 25565 for players.
To have the Minecraft server start whenever Windows boots, enable **Settings → Start the Minecraft server when the manager starts**.

Remove it with `.\uninstall-service.ps1` (add `-RemoveFirewallRules` to remove the firewall rules too). Your worlds, backups and settings are kept.

> The service runs as **LocalSystem**, so Java must be reachable by that account. Choosing a full path to `java.exe` in Settings (as **Detect** does) avoids PATH issues.

## Remote access and HTTPS

By default the panel only listens on `localhost`. To manage it from another machine, edit `appsettings.json`:

```json
"Urls": "http://0.0.0.0:8080"
```

then restart the service and open the port with `.\install-service.ps1 -OpenPanelPort`, or create the firewall rule yourself.

Plain HTTP sends your password unencrypted, so for anything beyond a trusted LAN use HTTPS. Either put the panel behind a
reverse proxy (IIS, Caddy, nginx), or give Kestrel a certificate directly by replacing `Urls` with:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://0.0.0.0:8443",
      "Certificate": { "Path": "C:\\certs\\panel.pfx", "Password": "<pfx password>" }
    }
  }
}
```

## Where things are stored

| Path | Contents |
| --- | --- |
| `<install folder>\data\settings.json` | Panel settings and the admin password hash |
| `<install folder>\data\keys\` | Keys that protect sign-in cookies |
| `<server directory>\` | `server.jar`, `server.properties`, `eula.txt`, worlds, `ops.json`, etc. |
| `<server directory>\backups\` | World backups (configurable in Settings, ideally on another drive) |
| `<server directory>\.manager\jars\` | The last 5 replaced server jars, for rollback |

Set `Manager:DataDirectory` in `appsettings.json` to keep panel data elsewhere.
**Forgot the password?** Stop the service, delete `AdminPasswordHash` from `data\settings.json` (or delete the file), start it again and you'll be taken to setup.

## How a few things work

- **Creating worlds**: vanilla Minecraft generates a world the first time it starts with a new `level-name`. Creating a world therefore writes its settings to `server.properties`, makes it active and (optionally) restarts the server, which then generates it. Watch progress on the Console page.
- **Backups** are zips named `<world>_<yyyy-MM-dd_HH-mm-ss>_<kind>.zip`. Retention only prunes *scheduled* backups; manual and safety backups (before update, restore or delete) are kept until you remove them. `session.lock` is skipped because the running server holds it locked.
- **Downgrading** to an older Minecraft version is allowed but flagged: worlds opened in a newer version may not load in an older one, so restore a matching backup if needed.
- The **properties editor** shows every key that is actually in your `server.properties`. Minecraft adds and removes keys between versions; keys the panel doesn't recognise are still editable as text.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). You can build the Windows release on Windows, macOS or Linux:

```powershell
.\scripts\publish.ps1            # Windows → dist\MinecraftServerManager-win-x64.zip
```
```bash
scripts/publish.sh               # macOS / Linux → dist/MinecraftServerManager-win-x64.zip
```

Run locally for development (works on macOS/Linux too):

```bash
dotnet run --project src/MinecraftServerManager      # http://localhost:5031
dotnet test                                          # unit + integration tests
```

## Project layout

```
src/MinecraftServerManager/
  Program.cs                 Host setup: Windows service, auth, DI, endpoints
  Services/                  Server process, properties, worlds, backups, updates, players, auth, Java detection
  Components/Pages/          Dashboard, Console, Properties, Worlds, Backups, Updates, Players, Settings
  Components/Account/        Sign-in and first-run setup (static server-rendered)
  Endpoints/                 Backup downloads, world uploads, sign-out
  wwwroot/                   Styles and small JS helpers
scripts/                     publish, install-service, uninstall-service
tests/                       xUnit tests
tools/fake-java/             Stand-in for java.exe to test the panel without Java or a real server
docs/                        Wiki source, Word user guide and its generator
```

Built with ASP.NET Core Blazor (interactive server rendering) on .NET 10.

## Limitations

- Manages the **vanilla** Mojang server. Paper, Fabric and Forge jars can be launched (set the jar file name in Settings), but the Updates page only installs vanilla.
- One Minecraft server per panel instance. To manage several servers, run multiple copies of the panel with different install folders and ports.
- *Minecraft* is a trademark of Mojang AB. This project is not affiliated with Mojang or Microsoft.
