# Quick Start

This gets you from the download to a running Minecraft server in about ten minutes. The rest of the guide covers each step in more detail.

## Before you begin

You need:

- A Windows Server 2016+ or Windows 10/11 (x64) machine with administrator access.
- The release file `MinecraftServerManager-win-x64.zip`.
- Internet access from the server (to download Minecraft and Java).

## Steps

1. **Install Java.** Download and install a 64-bit JDK from [Eclipse Temurin](https://adoptium.net/temurin/releases/). Choose **Java 21** for Minecraft 1.20.5 to 1.21.x, or **Java 25** for Minecraft 26.x. Tick *Set JAVA_HOME* during setup.
2. **Extract the release** to a permanent folder, for example `C:\MinecraftManager`.
3. **Install the service.** Open PowerShell **as administrator** in that folder and run:

   ```powershell
   .\install-service.ps1 -OpenMinecraftPort
   ```

4. **Open the panel** at <http://localhost:8080> in a browser on the server.
5. **Create your admin account** and confirm the server directory (default `C:\MinecraftServer`). See [Installation](Installation#first-run-setup).
6. **Select Java.** Go to **Settings → Java**, click **Detect installed Java**, choose the version you installed, then **Save settings**.
7. **Install Minecraft.** Go to **Updates** and click **Install** next to the latest release your Java supports.
8. **Accept the EULA.** Read the [Minecraft EULA](https://aka.ms/MinecraftEULA) and click **I agree** on the Dashboard or Updates page.
9. **Start the server** with the **Start** button on the Dashboard. The first start generates the world; watch progress on the [Console](Console).
10. **Connect** from Minecraft: *Multiplayer → Add Server* with the server's IP address (port 25565 is the default).

## What next?

- Turn on **Start the Minecraft server when the manager starts** in [Settings](Settings) so the server comes back after a reboot.
- Turn on scheduled backups on the [Backups](Backups) page.
- Review [Server Properties](Server-Properties): set the MOTD, difficulty and whitelist.
- To manage the panel from your own PC, see [Remote Access and HTTPS](Remote-Access-and-HTTPS).
