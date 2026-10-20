# Configuration Reference

The panel is configured in two places:

| File | Edited by | Holds |
| --- | --- | --- |
| `appsettings.json` (panel folder) | You, in a text editor | Start-up settings: listening address, data folder, logging, HTTPS. Restart the panel after changing it. |
| `data\settings.json` (data folder) | The panel ([Settings](Settings), [Backups](Backups) pages) | Everything about the Minecraft server: folders, Java, memory, backups, your account. Normally you don't edit this by hand. |

## appsettings.json

The default file:

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

| Key | Default | Description |
| --- | --- | --- |
| `Urls` | `http://localhost:8080` | Address(es) the panel listens on. Separate several with `;`, e.g. `http://localhost:8080;http://192.168.1.20:8080`. Use `0.0.0.0` for all interfaces. See [Remote Access and HTTPS](Remote-Access-and-HTTPS). |
| `Manager:DataDirectory` | *(blank)* | Folder for panel data (`settings.json`, sign-in keys). Blank means a `data` folder next to the executable. Relative paths are relative to the panel folder. |
| `Logging:LogLevel` | see above | Log levels for the console window (when run interactively). Levels: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`. |
| `Logging:EventLog:LogLevel` | see above | Log levels written to the Windows Event Log when running as a service. |
| `AllowedHosts` | `*` | Host names the panel answers to. Set to your host name(s), separated by `;`, to reject requests for other names. |
| `Kestrel:Endpoints` | *(not set)* | HTTPS and advanced listener settings. Replaces `Urls` when present. See [Remote Access and HTTPS](Remote-Access-and-HTTPS#option-2-https-directly-in-the-panel). |

### Logging

To log more detail from the panel to the Event Log, for example while troubleshooting:

```json
"EventLog": { "LogLevel": { "Default": "Warning", "MinecraftServerManager": "Debug" } }
```

### Overriding settings without editing the file

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

## Command-line and environment summary

| Purpose | Example |
| --- | --- |
| Different port for a test run | `MinecraftServerManager.exe --Urls http://localhost:9090` |
| Separate data folder | `MinecraftServerManager.exe --Manager:DataDirectory D:\PanelData` |
| Detailed errors in the browser (testing only) | `$env:ASPNETCORE_ENVIRONMENT = "Development"` before starting |
