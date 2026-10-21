# Minecraft Server Manager

A web-based control panel for running a **Minecraft: Java Edition** dedicated server on **Windows Server**.
It installs with a setup wizard (no .NET install needed), runs as a Windows service, and is
managed from a browser, either on the server itself or remotely.

**Download:** [Releases on Gitea](https://gitea.uk.local:3001/Apps/minecraft-server-management/releases) ·
[Releases on GitHub](https://github.com/davegarner/minecraft-server-management/releases)

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

1. **Download** `MinecraftServerManager-<version>-Setup.exe` from the Releases page and run it on the server.
   Keep the defaults (panel port 8080, Minecraft players allowed through the firewall) and click **Install**.
   The installers aren't code-signed yet, so if SmartScreen warns, check the file against `SHA256SUMS.txt` and choose **More info → Run anyway**.
2. **Click Finish.** The panel opens at <http://localhost:8080>. It runs as a Windows service and starts with Windows.
3. **Create the admin account** and choose the server directory (default `C:\MinecraftServer`).
4. Go to **Settings → Java**, click **Detect installed Java**, pick the right Java and **Save**.
5. Go to **Updates** and install the latest release. Accept the Minecraft EULA when prompted.
6. Optionally adjust **Server Properties** or create a world on **Worlds**, then press **Start** on the Dashboard.

To have the Minecraft server start whenever Windows boots, enable **Settings → Start the Minecraft server when the manager starts**.
To upgrade, run the new version's setup wizard; your settings, worlds and backups are kept.

### Other ways to install

| Download | Use it for |
| --- | --- |
| `MinecraftServerManager-<version>-Setup.exe` | Most people: a setup wizard that installs and upgrades |
| `MinecraftServerManager-<version>-x64.msi` | Unattended installs, Group Policy or Intune: `msiexec /i <file> /qn PANELPORT=8080 REMOTEACCESS=0 OPENMINECRAFTPORT=1` |
| `MinecraftServerManager-<version>-win-x64.zip` | Portable or multiple instances: extract it, then run `.\install-service.ps1 -OpenMinecraftPort` from an elevated PowerShell prompt |

See [Installation](https://gitea.uk.local:3001/Apps/minecraft-server-management/wiki/Installation) in the wiki for details and silent-install switches.

> The service runs as its own virtual account, `NT SERVICE\MinecraftServerManager`, so Java must be installed system-wide (not inside a user's profile). Choosing a full path to `java.exe` in Settings (as **Detect** does) avoids PATH issues.

## Remote access and HTTPS

By default the panel only listens on `localhost`. To manage it from another machine, tick *Allow other computers to open the web panel* in the setup wizard, or afterwards run this from an elevated PowerShell prompt:

```powershell
& "C:\Program Files\Minecraft Server Manager\MinecraftServerManager.exe" configure --remote-access
Restart-Service MinecraftServerManager
```

and allow the port through Windows Firewall. Settings live in `C:\ProgramData\Minecraft Server Manager\appsettings.json`, which upgrades don't overwrite (with the zip, edit the `appsettings.json` next to the executable).

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
| `C:\Program Files\Minecraft Server Manager\` | The program (replaced on upgrade) |
| `C:\ProgramData\Minecraft Server Manager\appsettings.json` | Panel address, port and HTTPS settings |
| `C:\ProgramData\Minecraft Server Manager\data\settings.json` | Panel settings and the admin password hash |
| `C:\ProgramData\Minecraft Server Manager\data\keys\` | Keys that protect sign-in cookies |
| `<server directory>\` | `server.jar`, `server.properties`, `eula.txt`, worlds, `ops.json`, etc. |
| `<server directory>\backups\` | World backups (configurable in Settings, ideally on another drive) |
| `<server directory>\.manager\jars\` | The last 5 replaced server jars, for rollback |

With the zip, panel data is in `data\` next to the executable. Set `Manager:DataDirectory` in `appsettings.json` to keep it elsewhere.
**Forgot the password?** Stop the service, delete `AdminPasswordHash` from `data\settings.json` (or delete the file), start it again and you'll be taken to setup.

## How a few things work

- **Creating worlds**: vanilla Minecraft generates a world the first time it starts with a new `level-name`. Creating a world therefore writes its settings to `server.properties`, makes it active and (optionally) restarts the server, which then generates it. Watch progress on the Console page.
- **Backups** are zips named `<world>_<yyyy-MM-dd_HH-mm-ss>_<kind>.zip`. Retention only prunes *scheduled* backups; manual and safety backups (before update, restore or delete) are kept until you remove them. `session.lock` is skipped because the running server holds it locked.
- **Downgrading** to an older Minecraft version is allowed but flagged: worlds opened in a newer version may not load in an older one, so restore a matching backup if needed.
- The **properties editor** shows every key that is actually in your `server.properties`. Minecraft adds and removes keys between versions; keys the panel doesn't recognise are still editable as text.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). To build the setup wizard, MSI, zip and checksums
(Windows, with [Inno Setup 6.3+](https://jrsoftware.org/isdl.php); WiX comes from NuGet):

```powershell
.\scripts\build-release.ps1      # → artifacts\release\
```

The portable zip alone can be built on Windows, macOS or Linux:

```powershell
.\scripts\publish.ps1            # Windows → dist\MinecraftServerManager-win-x64.zip
```
```bash
scripts/publish.sh               # macOS / Linux → dist/MinecraftServerManager-win-x64.zip
```

Pushing a `v*` tag builds, tests and publishes a GitHub release (`.github/workflows/release.yml`); see
[Development](https://gitea.uk.local:3001/Apps/minecraft-server-management/wiki/Development#release-workflow).

Run locally for development (works on macOS/Linux too):

```bash
dotnet run --project src/MinecraftServerManager      # http://localhost:5031
dotnet test                                          # unit + integration tests
```

## Project layout

```
src/MinecraftServerManager/
  Program.cs                 Host setup: Windows service, auth, endpoints
  Features/                  One folder per area: server process, properties, worlds, backups, updates, players, auth, Java detection
  Core/                      Rules shared by features (world folder and player name rules)
  Infrastructure/            Mojang API client, Windows job objects
  Components/Pages/          Dashboard, Console, Properties, Worlds, Backups, Updates, Players, Settings
  Components/Account/        Sign-in and first-run setup (static server-rendered)
  Endpoints/                 Backup downloads, world uploads, sign-out
  Hosting/                   Service registration and the configure command used by the installers
  wwwroot/                   Styles and small JS helpers
installer/                   Setup wizard (Inno Setup), MSI (WiX), installer tests, release notes template
scripts/                     build-release, publish, install-service, uninstall-service
tests/                       xUnit tests
tools/fake-java/             Stand-in for java.exe to test the panel without Java or a real server
docs/                        Wiki source, Word user guide and its generator
```

Built with ASP.NET Core Blazor (interactive server rendering) on .NET 10.

## Limitations

- Manages the **vanilla** Mojang server. Paper, Fabric and Forge jars can be launched (set the jar file name in Settings), but the Updates page only installs vanilla.
- One Minecraft server per panel instance. To manage several servers, run multiple copies of the panel from the zip with different folders, ports and service names.
- *Minecraft* is a trademark of Mojang AB. This project is not affiliated with Mojang or Microsoft.
