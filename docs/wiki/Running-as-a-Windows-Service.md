# Running as a Windows Service

The panel runs as a Windows service, so it starts with Windows, keeps running when nobody is signed in, and shuts the Minecraft server down cleanly when Windows shuts down.

**If you used the setup wizard or the MSI, this is already done.** The installer registers the service, sets it to start automatically and restart after a failure, and adds the firewall rules you chose. This page explains how to manage the service, and how to install it by hand from the zip.

## The service

| Property | Value |
| --- | --- |
| Name | `MinecraftServerManager` |
| Display name | Minecraft Server Manager |
| Startup type | Automatic |
| Log on as | `NT SERVICE\MinecraftServerManager`, the service's own virtual account ([details](#service-account)) |
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

The service runs as **`NT SERVICE\MinecraftServerManager`**, a virtual account Windows manages for the service (it has no password and can't be used to sign in). It has the rights of an ordinary user plus access to the panel's own folders, so the panel and Minecraft can't change the rest of the machine. Versions before 1.0.3 ran as Local System; upgrading switches to the virtual account.

When installing or upgrading, the installers (and `install-service.ps1`) run `MinecraftServerManager.exe secure`, which:

- limits `C:\ProgramData\Minecraft Server Manager` (settings, sign-in keys) to SYSTEM, Administrators and the service;
- gives the service **Modify** access to the server directory and backup directory, and stops other users changing them if every user could (as with folders at the root of a drive);
- with the zip, gives the service access to the panel folder, and limits it the same way if every user could change it;
- registers the panel's Event Log source.

Folders the panel creates itself (the server directory at first-run setup, a new backup directory) get the same permissions. Opening them in Explorer as an administrator may show *You don't currently have permission*; click **Continue**.

**Java** must be readable by the service: a system-wide installation (e.g. Eclipse Temurin in `C:\Program Files`) works; a JDK inside a user's profile does not.

To use a different account, pass it to the script; it's kept on later upgrades with the setup wizard (the MSI always uses the virtual account):

```powershell
.\install-service.ps1 -Account LocalSystem            # the behaviour of versions before 1.0.3
.\install-service.ps1 -Account ".\mcservice"          # then set its password:
sc.exe config MinecraftServerManager obj= ".\mcservice" password= "<password>"
```

A custom account needs **Modify** permission on `C:\ProgramData\Minecraft Server Manager` (or the zip folder), the server directory and the backup directory, and **Read & execute** on the program folder and Java.

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
