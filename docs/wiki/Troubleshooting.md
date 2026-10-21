# Troubleshooting

Start with the **[Console](Console)**: most problems are explained there, either by Minecraft or by a blue `[Manager]` message. For the panel itself, check **Event Viewer → Windows Logs → Application** (source *MinecraftServerManager*).

## Installing

| Symptom | Fix |
| --- | --- |
| *Windows protected your PC* (SmartScreen) | The installers aren't code-signed yet. Check the file's SHA-256 (`Get-FileHash <file>`) against `SHA256SUMS.txt` from the release, then click **More info → Run anyway**. |
| *Setting up the Windows service failed (exit code N)* | The files were installed but the service didn't start. Usually the panel's port is taken: see *Port already in use?* below, then run `.\install-service.ps1 -PanelPort <port>` from `C:\Program Files\Minecraft Server Manager` in an administrator PowerShell window. Check Event Viewer for the panel's own error. |
| *Saving the panel settings failed* | `C:\ProgramData\Minecraft Server Manager\appsettings.json` couldn't be written; it may contain invalid JSON. Fix or delete it and run the installer again. |
| Need a log | Setup wizard: run it with `/LOG="setup.log"`. MSI: `msiexec /i <file>.msi /l*v install.log`. |

## The panel won't open in the browser

| Check | How |
| --- | --- |
| Is the service running? | `Get-Service MinecraftServerManager`. Start it with `Start-Service MinecraftServerManager`. |
| Right address? | Use the Start menu shortcut, or check `Urls` in `C:\ProgramData\Minecraft Server Manager\appsettings.json` (default `http://localhost:8080`, which only works **on the server itself**). |
| Port already in use? | If the service stops straight away, check Event Viewer for "address already in use" (or run `netstat -ano \| findstr :8080`). Move the panel with [`configure --port 9090`](Configuration-Reference#the-configure-command) and restart the service. |
| Remote access blocked? | See [Remote Access and HTTPS](Remote-Access-and-HTTPS): listen on `0.0.0.0` (`configure --remote-access`) **and** open the firewall. |
| Run it interactively | Stop the service and run `MinecraftServerManager.exe` from an administrator PowerShell window to see start-up messages and errors. |

## "Rejoining the server…" keeps appearing

The page lost its live connection, usually because the panel restarted or the network dropped. It reconnects automatically; if not, click **Retry** or reload. Behind a reverse proxy, make sure **WebSockets** are enabled ([details](Remote-Access-and-HTTPS#option-3-behind-a-reverse-proxy)).

## The server won't start

| Message | Fix |
| --- | --- |
| *No server jar was found* | Install a version on [Updates](Updates), or fix *Server jar file name* in [Settings](Settings#server). |
| *The Minecraft EULA has not been accepted* | Click **I agree** on the Dashboard or Updates page after reading the EULA. |
| *Could not launch Java using '…'* | The Java path is wrong. Use **Detect installed Java** in [Settings](Settings#java). |
| *The server directory '…' does not exist* | Fix the path in Settings. |
| Console: `UnsupportedClassVersionError … class file version 65.0` | Java is too old: 65 = Java 21, 69 = Java 25, 61 = Java 17. Install the right Java and select it. |
| Console: `Invalid maximum heap size` / `Could not reserve enough space` | The memory setting is too large for the machine. Lower *Maximum heap*. |
| Console: `Unrecognized VM option` | Remove the bad option from *Extra JVM arguments*. |

## The server is stuck on "Starting…"

The panel shows **Running** when Minecraft logs `Done (…)! For help, type "help"`. Generating a new world or upgrading a big world after an update can take several minutes. Watch the console for progress. If nothing has happened for a long time, click **Kill** and check `logs\latest.log`.

## Players can't connect

1. **Firewall:** TCP **25565** (or your `server-port`) must be allowed inbound. The installers add this rule unless you unticked it; see [Installation](Installation#open-the-firewall-for-players).
2. **Router:** players outside your network need TCP 25565 forwarded to the server.
3. **Port conflict:** the console shows `**** FAILED TO BIND TO PORT!` if something else uses the port. Change `server-port` or stop the other program.
4. **Whitelist:** if `white-list` is on, add the player on [Players](Players).
5. **Version mismatch:** the player's game version must match the server's (see *Installed* on [Updates](Updates)).
6. **"Failed to verify username":** the player isn't signed in to a genuine Minecraft account while `online-mode=true`.

## The server crashes

- The Dashboard shows *The server stopped unexpectedly* with the exit code. Check the red lines in the [Console](Console) and the newest file in `crash-reports\`.
- If **Restart automatically after a crash** is on, the panel restarts the server after 10 seconds. After **3 crashes within 10 minutes** it stops retrying (the console says so) so a broken setup doesn't loop. Fix the cause and click **Start**.
- **Out of memory** (`java.lang.OutOfMemoryError`): increase *Maximum heap* in [Settings](Settings#memory-and-jvm), or reduce `view-distance`/`simulation-distance`.
- **Mods or plugins** (non-vanilla jars): remove recently added ones to find the culprit.

## The server is slow / "Can't keep up!"

- Lower `view-distance` (e.g. 8–10) and `simulation-distance` (e.g. 6–8) in [Server Properties](Server-Properties).
- Give Java enough memory, and try **Use recommended G1GC flags** in [Settings](Settings#memory-and-jvm).
- Check the CPU and Memory cards on the Dashboard, and run `tick query` on the Console.
- Pre-generated or very large worlds need fast disks; avoid network drives for the server directory.

## Backup problems

| Symptom | Cause and fix |
| --- | --- |
| *The server is starting or stopping. Try the backup again in a moment.* | Backups of the running world need the server fully started. Wait for **Running**. |
| *Not enough free disk space for the backup* | Free space, delete old backups, or move backups to another drive in [Settings](Settings#server). |
| Event log: *Timed out waiting for the server to confirm the save* | Minecraft didn't confirm the save within 2 minutes (very busy server). The backup still completed; check it, and consider backing up at a quieter time. |
| Event log: *skipped … locked file(s)* | A file was locked by another program (often antivirus or a sync tool). Exclude the server directory from real-time scanning. |
| Scheduled backups don't run | Check the schedule is on and saved. With *Only while the server is running*, nothing happens while the server is stopped. |
| Restore fails | The original world is put back automatically. Check the error; the zip must contain the world folder at its top level. |

## Update problems

| Symptom | Fix |
| --- | --- |
| *Couldn't reach Mojang's version service* | The server has no internet access, or a proxy/firewall blocks `piston-meta.mojang.com`. |
| *requires Java N or newer* | Install that Java and select it in [Settings](Settings#java). |
| *failed SHA-1 verification* | The download was corrupted or altered in transit. Try again; if it persists, check for a proxy rewriting downloads. The existing jar is untouched. |
| The world won't load after a downgrade | Restore the *Before update* backup from [Backups](Backups) and the matching jar from *Previous server jars*. |

## Player list problems

| Symptom | Fix |
| --- | --- |
| *No Minecraft account named '…' exists* | Check the spelling. With the server stopped and `online-mode=true`, names are checked against Mojang's account service. |
| Change didn't stick | While the server runs, changes are made with console commands; check the console for the response (for example *That player does not exist*). |
| Online list looks wrong | Click **Refresh** on [Players](Players) (runs `list`). |

## Signing in

| Symptom | Fix |
| --- | --- |
| *Too many failed attempts* | Wait 5 minutes. |
| Forgot the password | See [Security → Resetting a forgotten password](Security#resetting-a-forgotten-password). |
| Signed out unexpectedly | The password was changed, `data\keys\` was removed, or *Keep me signed in* wasn't ticked and the browser was closed. |

## Collecting information for support

- The Minecraft version (Updates), Java version (Settings → Test), and panel version (Settings → Account).
- The relevant [Console](Console) lines, `logs\latest.log`, and any file in `crash-reports\`.
- Panel errors from Event Viewer (Application log, source *MinecraftServerManager*).
