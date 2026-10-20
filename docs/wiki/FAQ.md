# FAQ

### Does closing the browser stop the server?
No. The browser is only a window onto the panel. The server keeps running for as long as the panel (normally a Windows service) runs.

### What happens when Windows restarts?
The service stops the Minecraft server cleanly during shutdown. After the restart the panel starts automatically, and so does Minecraft if **Start the Minecraft server when the manager starts** is on in [Settings](Settings#server).

### Can I run Paper, Spigot, Fabric or Forge?
The panel can **launch** any server jar: place it in the server directory and set *Server jar file name* in [Settings](Settings#server). Fabric and Forge need their installer run first. [Updates](Updates) only installs and updates the vanilla Mojang server, so update other jars by hand. The [Worlds](Worlds) and [Backups](Backups) pages understand Paper's separate `world_nether` / `world_the_end` folders.

### Does it support Bedrock Edition?
No. It manages the **Java Edition** dedicated server only.

### Can I manage several Minecraft servers?
One panel manages one Minecraft server. Run one panel per server, each with its own folder, port and service name. See [Running as a Windows Service](Running-as-a-Windows-Service#running-more-than-one-minecraft-server).

### Can more than one person use the panel?
There's a single admin account, which several people can share. Everyone sees the same live state, so two people can have it open at once. Give the password only to people you'd trust with administrator access to the server.

### How much memory should I give the server?
For a small vanilla server (up to ~10 players) 4–6 GB is plenty; larger or modded servers may need 8–12 GB. Leave at least 2 GB for Windows. Setting minimum and maximum heap equal (the recommended-flags button does this) gives the most predictable performance.

### Where are my worlds and backups?
Worlds are folders in the server directory (default `C:\MinecraftServer`); backups are in `backups` inside it unless you moved them. See [Files and Folders](Files-and-Folders).

### Can I edit files by hand?
Yes, but preferably while the server is stopped. See [Files and Folders → Editing files by hand](Files-and-Folders#editing-files-by-hand).

### How do I change game rules (keepInventory etc.)?
Game rules are stored in each world, not in `server.properties`. Use the [Console](Console): `gamerule keepInventory true`.

### Do I need RCON?
No. The panel talks to the server directly. Leave `enable-rcon=false` unless another tool needs it.

### Is my world safe while a backup runs?
Yes. Autosave is paused and everything is flushed to disk first, then turned back on afterwards, and players can keep playing. See [Backups](Backups#backing-up-a-running-world-safely).

### How do I move to a new server machine?
Install the panel on the new machine, stop both servers, copy the server directory (and optionally `data\` from the panel folder) across, then select Java in [Settings](Settings#java) and start.

### How do I update the panel itself?
See [Running as a Windows Service → Updating the panel](Running-as-a-Windows-Service#updating-the-panel).

### Can I use it on Linux or macOS?
It's built for Windows, but the panel is cross-platform .NET, and it can be built and run on Linux or macOS for testing (see [Development](Development)). Windows-specific features (the service and the job object) only apply on Windows.
