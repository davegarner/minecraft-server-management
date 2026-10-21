# Configuration Reference

The panel's own settings live in a few files. Settings about the Minecraft server itself (folders, Java, memory, backups) are changed in the panel and stored in `settings.json`.

| File | Edited by | Holds |
| --- | --- | --- |
| `C:\ProgramData\Minecraft Server Manager\appsettings.json` | The installer, [`configure`](#the-configure-command), or you | Start-up settings for an installed panel: listening address, data folder, HTTPS. **Kept when you upgrade.** |
| `appsettings.json` next to `MinecraftServerManager.exe` | Shipped with the release | Defaults (and logging levels). Replaced when you upgrade an installed panel; with the zip, edit this one. |
| `<data folder>\settings.json` | The panel ([Settings](Settings), [Backups](Backups) pages) | Everything about the Minecraft server, plus your account. Normally you don't edit this by hand. |

The data folder is `C:\ProgramData\Minecraft Server Manager\data` when installed with the setup wizard or MSI, and `data` next to the executable for the zip.

Restart the service after editing an `appsettings.json` file: `Restart-Service MinecraftServerManager`.

## Order of precedence

Later sources override earlier ones:

1. `appsettings.json` next to the executable
2. `C:\ProgramData\Minecraft Server Manager\appsettings.json`
3. Environment variables (e.g. `Manager__DataDirectory`)
4. Command-line arguments (e.g. `--Urls http://localhost:9090`)

## The configure command

`MinecraftServerManager.exe configure` updates `C:\ProgramData\Minecraft Server Manager\appsettings.json` without editing JSON by hand. Run it from an **administrator** PowerShell window, then restart the service.

```powershell
$exe = "C:\Program Files\Minecraft Server Manager\MinecraftServerManager.exe"
& $exe configure --port 9090          # move the panel to port 9090
& $exe configure --remote-access      # listen on all network interfaces (http://0.0.0.0:<port>)
& $exe configure --local-only         # listen on localhost only
Restart-Service MinecraftServerManager
```

| Option | Effect |
| --- | --- |
| `--port <1-65535>` | Sets the panel's port, keeping local/remote as it is. |
| `--remote-access` | Listen on all network interfaces. Also open the port in Windows Firewall ([Remote Access and HTTPS](Remote-Access-and-HTTPS)). |
| `--local-only` | Listen on `localhost` only (the default). |
| `--keep-existing` | Don't change an existing address (used by the installers so upgrades keep your choice). |

It also sets `Manager:DataDirectory` to `C:\ProgramData\Minecraft Server Manager\data` if no data folder is configured, and keeps any other keys in the file. It returns exit code 0 on success and 2 for invalid arguments.

> If you change the port, update the Start menu shortcut too (right-click → Properties), or re-run the setup wizard.

## appsettings.json keys

The default file next to the executable:

```json
{
  "Urls": "http://localhost:8080",
  "Manager": {
    "DataDirectory": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "EventLog": {
      "LogLevel": {
        "Default": "Warning",
        "MinecraftServerManager": "Information"
      }
    }
  },
  "AllowedHosts": "*"
}
```

A typical installer-written ProgramData file:

```json
{
  "Urls": "http://localhost:8080",
  "Manager": {
    "DataDirectory": "C:\\ProgramData\\Minecraft Server Manager\\data"
  }
}
```

| Key | Default | Description |
| --- | --- | --- |
| `Urls` | `http://localhost:8080` | Address(es) the panel listens on. Separate several with `;`, e.g. `http://localhost:8080;http://192.168.1.20:8080`. Use `0.0.0.0` for all interfaces. See [Remote Access and HTTPS](Remote-Access-and-HTTPS). |
| `Manager:DataDirectory` | *(blank)* | Folder for panel data (`settings.json`, sign-in keys). Blank means a `data` folder next to the executable. Relative paths are relative to the panel folder. |
| `Logging:LogLevel` | see above | Log levels for the console window (when run interactively). Levels: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. |
| `Logging:EventLog:LogLevel` | see above | Log levels written to the Windows Event Log when running as a service. |
| `AllowedHosts` | `*` | Host names the panel answers to. Set to your host name(s), separated by `;`, to reject requests for other names. |
| `Kestrel:Endpoints` | *(not set)* | HTTPS and advanced listener settings. Replaces `Urls` when present. See [Remote Access and HTTPS](Remote-Access-and-HTTPS#option-2-https-directly-in-the-panel). |

### Logging

To log more detail from the panel to the Event Log, for example while troubleshooting, add this to the ProgramData `appsettings.json`:

```json
"Logging": { "EventLog": { "LogLevel": { "Default": "Warning", "MinecraftServerManager": "Debug" } } }
```

### Overriding settings without editing a file

Any key can be overridden on the command line or with environment variables, which is handy for testing:

```powershell
.\MinecraftServerManager.exe --Urls "http://localhost:9090" --Manager:DataDirectory "D:\PanelData"
$env:Manager__DataDirectory = "D:\PanelData"   # environment variable form (double underscore)
```

## settings.json

Stored in the data folder and written by the panel. Each value and where it's set:

| Key | Default | Set on | Meaning |
| --- | --- | --- | --- |
| `ServerDirectory` | `C:\MinecraftServer` | Settings → Server, Setup | Minecraft server folder |
| `JarFileName` | `server.jar` | Settings → Server | Jar launched with `java -jar` |
| `JavaPath` | `java` | Settings → Java | Java executable |
| `MinMemoryMb` | `1024` | Settings → Memory | `-Xms` in MB |
| `MaxMemoryMb` | `4096` | Settings → Memory | `-Xmx` in MB |
| `JvmArguments` | *(blank)* | Settings → Memory | Extra JVM options |
| `ServerArguments` | `nogui` | Settings → Memory | Arguments after the jar |
| `AutoStartServer` | `false` | Settings → Server | Start Minecraft when the panel starts |
| `AutoRestartOnCrash` | `true` | Settings → Server | Restart after a crash (max 3 in 10 min) |
| `StopTimeoutSeconds` | `60` | Settings → Server | Wait for a clean stop before killing |
| `BackupDirectory` | *(blank: `<server>\backups`)* | Settings → Server | Backup folder |
| `ScheduledBackupsEnabled` | `false` | Backups → Schedule | Turn the schedule on |
| `BackupIntervalMinutes` | `360` | Backups → Schedule | Time between scheduled backups |
| `BackupRetentionCount` | `10` | Backups → Schedule | Scheduled backups to keep per world |
| `BackupOnlyWhenRunning` | `true` | Backups → Schedule | Skip while the server is stopped |
| `AnnounceBackups` | `true` | Backups → Schedule | Announce backups in chat |
| `BackupBeforeUpdate` | `true` | Backups → Schedule | Back up before updates |
| `LastScheduledBackup` | *(none)* | *(automatic)* | When the last scheduled backup ran |
| `AdminUsername` | `admin` | Setup | Sign-in name |
| `AdminPasswordHash` | *(set at setup)* | Setup, Settings → Account | PBKDF2 hash of the password (never the password itself) |

If `settings.json` can't be read (for example it was damaged), the panel saves a copy as `settings.json.corrupt-<timestamp>` and starts with defaults, which also means running setup again.

## Installer options

The setup wizard and MSI accept options for unattended installs (panel port, firewall rules, remote access). See [Installation → Unattended installation](Installation#unattended-installation).
