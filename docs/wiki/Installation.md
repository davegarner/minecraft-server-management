# Installation

This page covers system requirements, installing the panel, the first-run setup wizard, Java, and opening the firewall for players.

## Requirements

| Item | Requirement |
| --- | --- |
| Operating system | Windows Server 2016, 2019, 2022 or 2025, or Windows 10/11, 64-bit (x64) |
| .NET | **Not required.** The panel is self-contained. |
| Java | A 64-bit Java runtime matching your Minecraft version (see below) |
| Memory | Enough for Windows plus the Minecraft heap. 8 GB total is a sensible minimum for a small server. |
| Disk | About 120 MB for the panel, plus worlds and backups (allow several GB) |
| Network | Internet access to download Minecraft and check for updates. TCP port **25565** open for players. |
| Browser | Any current browser (Edge, Chrome, Firefox, Safari) |

### Which Java do I need?

The panel checks this for you before installing a version, and the [Updates](Updates) page shows the requirement for the latest release.

| Minecraft version | Minimum Java |
| --- | --- |
| 26.1 and later | Java 25 |
| 1.20.5 – 1.21.x | Java 21 |
| 1.18 – 1.20.4 | Java 17 |
| 1.17.x | Java 16 |
| 1.16.5 and earlier | Java 8 |

Install a **JDK or JRE from [Eclipse Temurin](https://adoptium.net/temurin/releases/)** (recommended), Microsoft Build of OpenJDK, Azul Zulu or Amazon Corretto. You can install several versions side by side and choose one in [Settings](Settings).

## Install the panel

1. Copy `MinecraftServerManager-win-x64.zip` to the server.
2. Right-click it, choose **Properties**, tick **Unblock** if shown, then **Extract All…** to a permanent folder such as `C:\MinecraftManager`.

   The folder contains:

   | File | Purpose |
   | --- | --- |
   | `MinecraftServerManager.exe` | The panel (web server and Minecraft supervisor) |
   | `appsettings.json` | Start-up configuration: listening address, logging ([reference](Configuration-Reference)) |
   | `wwwroot\` | Web assets |
   | `install-service.ps1`, `uninstall-service.ps1` | Windows service helpers |
   | `web.config` | Only used if you host the panel under IIS |

3. Choose how to run it:
   - **As a Windows service (recommended).** It starts with Windows and needs nobody logged on. See [Running as a Windows Service](Running-as-a-Windows-Service).
   - **Interactively, for a quick test.** Double-click `MinecraftServerManager.exe`. A console window opens and shows `Now listening on: http://localhost:8080`. Closing the window stops the panel and shuts the Minecraft server down cleanly.

> **Tip:** Keep the panel folder and the Minecraft server directory separate (for example `C:\MinecraftManager` and `C:\MinecraftServer`). Updating the panel then never touches your worlds.

## First-run setup

Open <http://localhost:8080>. The first time, you're taken to the setup page.

![First-run setup](images/setup-wizard.png)

| Field | What to enter |
| --- | --- |
| **Admin username** | The name you'll sign in with (letters, numbers, `.`, `-`, `_`). |
| **Password / Confirm password** | At least 8 characters. Use a strong, unique password, especially if the panel will be reachable from other machines. |
| **Server directory** | A full path where `server.jar`, `server.properties` and worlds are kept, e.g. `C:\MinecraftServer`. It's created if it doesn't exist. |

Click **Create account & continue**. You're signed in and taken to the Dashboard, which shows what's still needed:

![Dashboard on first run](images/dashboard-first-run.png)

> **Note:** There is a single administrator account. Anyone who can reach the setup page before it's completed could claim it, so complete setup straight after installing. By default the panel only listens on `localhost`, so this is only possible from the server itself.

## Choose Java

1. Go to **Settings**.
2. Under **Java**, click **Detect installed Java**. The panel searches `JAVA_HOME`, the `PATH`, and the usual install folders (`C:\Program Files\Eclipse Adoptium`, `Java`, `Microsoft`, `Zulu`, `Amazon Corretto`, `BellSoft`, `Semeru` and others).
3. Click the runtime you want. Its full path is filled in.
4. Click **Test** to confirm it runs, then **Save settings**.

See [Settings](Settings#java) for details.

## Install Minecraft and accept the EULA

1. Go to **Updates**.
2. Click **Install** on the version you want (usually the latest release your Java supports). The download comes from Mojang and is verified.
3. Read the [Minecraft End User License Agreement](https://aka.ms/MinecraftEULA). If you agree, click **I agree** in the blue banner. This writes `eula=true` to `eula.txt`; the server won't start without it.

![Updates page before anything is installed](images/updates-nothing-installed.png)

## Open the firewall for players

Players connect to TCP port **25565** (or whatever `server-port` is set to). Either:

- run `.\install-service.ps1 -OpenMinecraftPort`, or
- create the rule yourself:

  ```powershell
  New-NetFirewallRule -DisplayName "Minecraft Server (TCP 25565)" -Direction Inbound -Protocol TCP -LocalPort 25565 -Action Allow
  ```

For players outside your network, also forward TCP 25565 on your router or firewall to this server.

## Start the server

Click **Start** on the Dashboard. The first start downloads libraries and generates the world, which can take a minute or two. The status changes from **Starting…** to **Running** when Minecraft reports it's ready.

## Next steps

- [Running as a Windows Service](Running-as-a-Windows-Service), if you haven't already
- [Backups](Backups): turn on the schedule
- [Remote Access and HTTPS](Remote-Access-and-HTTPS): manage the panel from another PC
