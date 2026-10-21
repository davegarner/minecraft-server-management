# Running as a Windows Service

The panel runs as a Windows service, so it starts with Windows, keeps running when nobody is signed in, and shuts the Minecraft server down cleanly when Windows shuts down.

**If you used the setup wizard or the MSI, this is already done.** The installer registers the service, sets it to start automatically and restart after a failure, and adds the firewall rules you chose. This page explains how to manage the service, and how to install it by hand from the zip.

## The service

| Property | Value |
| --- | --- |
| Name | `MinecraftServerManager` |
| Display name | Minecraft Server Manager |
| Startup type | Automatic |
| Log on as | Local System |
| Recovery | Restart after 1 minute (failure count resets daily) |

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

- **Panel events** (start-up, errors, backups, updates, crashes) go to the Windows **Event Viewer → Windows Logs → Application**, source **MinecraftServerManager**. By default warnings and errors are logged, plus information from the panel itself. Change this in `C:\ProgramData\Minecraft Server Manager\appsettings.json` ([reference](Configuration-Reference#logging)).
- **Minecraft's own log** is in the server directory: `logs\latest.log` (older logs are zipped alongside), with crash reports in `crash-reports\`.

## Installing the service by hand (zip)

Open **PowerShell as administrator** in the folder containing `MinecraftServerManager.exe` and run:

```powershell
.\install-service.ps1 -OpenMinecraftPort
```

The script registers the service (or updates it if it already exists), sets the recovery options, adds any firewall rules you ask for, and starts it.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-ServiceName` | `MinecraftServerManager` | Service name, if you run more than one panel. |
| `-DisplayName` | `Minecraft Server Manager` | Name shown in *Services*. |
| `-OpenMinecraftPort` | off | Adds an inbound rule for TCP `-MinecraftPort` so players can connect. |
| `-MinecraftPort` | `25565` | Port for the rule above. Match `server-port`. |
| `-OpenPanelPort` | off | Adds an inbound rule for TCP `-PanelPort`, for remote access to the panel ([details](Remote-Access-and-HTTPS)). |
| `-PanelPort` | `8080` | Port for the rule above. |
| `-WaitForPanel` | off | Waits until the panel answers before finishing. |

> If PowerShell refuses to run the script, allow it for this session with `Set-ExecutionPolicy -Scope Process Bypass`, or unblock the file (*Properties → Unblock*).

To remove it: `.\uninstall-service.ps1` (add `-RemoveFirewallRules` to remove the firewall rules too). Worlds, backups and settings are kept.

## Service account

The service runs as **Local System**, which can read and write anywhere on the machine. That's the simplest setup. For tighter security you can run it as a dedicated local account:

1. Create a standard user, e.g. `mcservice`.
2. Give it **Modify** permission on `C:\ProgramData\Minecraft Server Manager` (or the zip folder), the server directory and the backup directory, and **Read & execute** on the program folder.
3. Make sure it can run Java (select a full Java path in [Settings](Settings#java)).
4. Change the service's log-on account:

   ```powershell
   sc.exe config MinecraftServerManager obj= ".\mcservice" password= "<password>"
   Restart-Service MinecraftServerManager
   ```

The panel's process (and therefore Minecraft) then only has that account's rights. Upgrading with the setup wizard keeps the service's log-on account.

## Updating the panel

- **Setup wizard / MSI:** run the new version's installer. See [Installation → Upgrading](Installation#upgrading).
- **Zip:** stop the service, copy the new files over the old ones **except** `appsettings.json` and the `data` folder, run `.\install-service.ps1` again, and it starts the service.

Your panel settings, password and sign-in keys are in the data folder; worlds and backups are in the server directory. Neither is touched by updating.

## Running more than one Minecraft server

Each panel manages one Minecraft server. To run several side by side, use the zip:

1. Extract the panel into a separate folder for each (e.g. `C:\MinecraftManager-Survival`, `C:\MinecraftManager-Creative`).
2. In each copy's `appsettings.json`, give `Urls` a different port.
3. Install each with a different `-ServiceName`.
4. In each panel, choose a different server directory and give each Minecraft server a different `server-port`.

The setup wizard and MSI install a single instance.
