# Security

The panel can start programs, run server commands and replace files, so treat access to it like administrator access to the server. This page describes how it protects itself and how to run it safely.

## Recommendations

1. **Keep the default `localhost` binding** unless you need remote access, and then prefer a VPN or HTTPS ([Remote Access and HTTPS](Remote-Access-and-HTTPS)).
2. **Use a strong, unique admin password** (the minimum is 8 characters; longer is better).
3. **Complete setup immediately after installing**. Until then, whoever completes the setup page first creates the admin account. The page only works in a browser on the server itself (`http://localhost:<port>`) unless you set `Manager:AllowRemoteSetup` ([Configuration Reference](Configuration-Reference#appsettingsjson-keys)).
4. **Don't enable RCON** unless another tool needs it, and never expose port 25575 to the internet. The panel doesn't need RCON.
5. **Consider a dedicated service account** rather than Local System ([how](Running-as-a-Windows-Service#service-account)).
6. **Restrict the folders**: only administrators and the service account need access to `C:\ProgramData\Minecraft Server Manager` (it holds `appsettings.json` and `data\`; with the zip, the panel folder). Ordinary users can read `C:\ProgramData` by default, so remove *Users* from this folder's permissions if others sign in to the server.
7. **Verify downloads** against `SHA256SUMS.txt` from the release before running an installer.
8. **Keep backups off the server** as well as on it.
9. **Keep Windows, Java, Minecraft and the panel updated.**

## Authentication

| Aspect | Behaviour |
| --- | --- |
| Accounts | One administrator, created during first-run setup. |
| Password storage | PBKDF2 with SHA-256, 210,000 iterations and a random 16-byte salt per password. Only the hash is stored, in `data\settings.json` ([where](Files-and-Folders#panel-data-folder)). |
| Brute-force protection | After 5 failed sign-ins from one IP address (for IPv6, one /64 network), sign-in from that address is refused for 5 minutes. |
| Session cookie | `mcm.auth`: HttpOnly, SameSite=Strict, encrypted and signed. Lasts for the browser session, or 14 days (renewed with use) with *Keep me signed in*. |
| Password change | Changing the password or username signs out every existing session: other browsers on their next page load, and panel tabs that are already open within 30 seconds. |
| Cookie keys | ASP.NET Core Data Protection keys in `data\keys\`. Deleting them signs everyone out. |

### Resetting a forgotten password

1. Stop the panel (`Stop-Service MinecraftServerManager`).
2. Open `C:\ProgramData\Minecraft Server Manager\data\settings.json` in Notepad as administrator (with the zip, `data\settings.json` next to the executable) and set `"AdminPasswordHash": ""`.
3. Start the panel and open it **from the server itself**. You'll get the setup page again; your worlds and settings are kept.

## Protection built into the panel

| Threat | Protection |
| --- | --- |
| Cross-site request forgery | All forms (sign-in, setup, sign-out, world upload) carry anti-forgery tokens; cookies are SameSite=Strict. |
| Unauthorised access | All management pages, the live connection's actions and the API endpoints require sign-in; only sign-in, setup and error pages are public. The setup page is disabled once an account exists. |
| Path traversal (downloads) | Backup downloads accept a plain file name only, resolved inside the backup folder. |
| "Zip slip" (uploads and restores) | Every entry in an uploaded world or restored backup is checked to stay inside its target folder; unsafe archives are rejected before anything is changed. |
| Archive bombs | An uploaded world or restored backup that would need more space than the disk has free is refused before extraction, and a world's `level.dat` is only read up to 32 MB. |
| Setup takeover | First-run setup is only offered to the server itself, checked by both the connection and the requested host name, so a web page can't reach it through DNS rebinding. |
| Command injection via player names | Player names are validated (1–16 letters, digits, underscores) and IP addresses parsed before being placed in commands. Line breaks are stripped from reasons and console input. |
| Tampered downloads | Server jars are verified against Mojang's SHA-1 before installation. |
| Orphaned processes | On Windows the Minecraft process is tied to the panel with a job object, so it can't keep running uncontrolled if the panel is killed. |

## What the panel sends over the internet

| Destination | When | What |
| --- | --- | --- |
| `piston-meta.mojang.com`, `piston-data.mojang.com` | Opening Updates/Dashboard, installing versions | Requests for the version list and server downloads. No personal data. |
| `api.mojang.com` | Adding a player to a list while the server is **stopped** and `online-mode=true` | The player name, to look up its UUID. |

The panel has no telemetry and makes no other outbound connections. (The Minecraft server itself contacts Mojang to authenticate players.)

## Logging and privacy

- The panel logs operational events (start/stop, backups, updates, errors) to the Windows Event Log.
- Passwords and sign-in cookies are never logged.
- Minecraft's own logs record player names and, with `log-ips=true`, IP addresses.
