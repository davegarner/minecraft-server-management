# Running as a Windows Service

Running the panel as a Windows service means it starts with Windows, keeps running when nobody is logged on, and shuts the Minecraft server down cleanly when Windows shuts down.

## Install the service

Open **PowerShell as administrator** in the folder containing `MinecraftServerManager.exe` and run:

```powershell
.\install-service.ps1 -OpenMinecraftPort
```

The script:

1. Registers a service named **MinecraftServerManager** ("Minecraft Server Manager") that starts **automatically**.
2. Sets recovery options so Windows restarts the service 1 minute after a failure (the failure count resets daily).
3. Optionally adds Windows Firewall rules.
4. Starts the service.

### Options

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-ServiceName` | `MinecraftServerManager` | Service name, if you run more than one panel. |
| `-DisplayName` | `Minecraft Server Manager` | Name shown in *Services*. |
| `-OpenMinecraftPort` | off | Adds an inbound rule for TCP `-MinecraftPort` (25565) so players can connect. |
| `-MinecraftPort` | `25565` | Port for the rule above. Match `server-port`. |
| `-OpenPanelPort` | off | Adds an inbound rule for TCP `-PanelPort`, for remote access to the panel. Also see [Remote Access and HTTPS](Remote-Access-and-HTTPS). |
| `-PanelPort` | `8080` | Port for the rule above. |

> If PowerShell refuses to run the script, allow it for this session with `Set-ExecutionPolicy -Scope Process Bypass`, or unblock the file (*Properties → Unblock*).

### Start Minecraft with Windows

The service starts the **panel** with Windows. To have the **Minecraft server** start too, turn on **Start the Minecraft server when the manager starts** in [Settings](Settings#server).

## Managing the service

Use *Services* (`services.msc`) or PowerShell:

```powershell
Get-Service MinecraftServerManager          # status
Restart-Service MinecraftServerManager      # restart the panel (Minecraft is stopped cleanly and started again if auto-start is on)
Stop-Service MinecraftServerManager         # stop the panel and the Minecraft server
Start-Service MinecraftServerManager
```

Stopping the service sends `stop` to Minecraft and waits for the world to save. The panel allows up to 90 seconds for this.

## Logs

- **Panel events** (start-up, errors, backups, updates, crashes) go to the Windows **Event Viewer → Windows Logs → Application**, source **MinecraftServerManager**. By default warnings and errors are logged, plus information from the panel itself. Change this in `appsettings.json` ([reference](Configuration-Reference#logging)).
- **Minecraft's own log** is in the server directory: `logs\latest.log` (older logs are zipped alongside), with crash reports in `crash-reports\`.

## Service account

The service runs as **Local System**, which can read and write anywhere on the machine. That's the simplest setup. For tighter security you can run it as a dedicated local account:

1. Create a standard user, e.g. `mcservice`.
2. Give it **Modify** permission on the panel folder, the server directory and the backup directory.
3. Make sure it can run Java (select a full Java path in [Settings](Settings#java)).
4. Change the service's log-on account:

   ```powershell
   sc.exe config MinecraftServerManager obj= ".\mcservice" password= "<password>"
   Restart-Service MinecraftServerManager
   ```

The panel's process (and therefore Minecraft) then only has that account's rights.

## Updating the panel

1. Stop the service: `Stop-Service MinecraftServerManager`.
2. Copy the new release's files over the old ones **except** `appsettings.json` (keep yours, but compare it with the new one for new options) and the `data` folder.
3. Start the service: `Start-Service MinecraftServerManager`.

Your panel settings, password and sign-in keys are in `data\`; worlds and backups are in the server directory. Neither is touched by updating.

## Uninstalling

```powershell
.\uninstall-service.ps1                       # stop (cleanly) and remove the service
.\uninstall-service.ps1 -RemoveFirewallRules  # ...and remove the firewall rules the installer added
```

Worlds, backups and settings are kept. Delete the folders yourself if you no longer need them.

## Running more than one Minecraft server

Each panel manages one Minecraft server. To run several:

1. Extract the panel into a separate folder for each (e.g. `C:\MinecraftManager-Survival`, `C:\MinecraftManager-Creative`).
2. In each copy's `appsettings.json`, give `Urls` a different port.
3. Install each with a different `-ServiceName`.
4. In each panel, choose a different server directory and give each Minecraft server a different `server-port`.
