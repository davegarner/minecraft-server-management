# Files and Folders

Where the panel and the Minecraft server keep everything, assuming the defaults (`C:\MinecraftServer` for Minecraft).

## Panel program folder

Installed by the setup wizard or MSI. Everything here is replaced when you upgrade, so don't keep your own files in it.

```text
C:\Program Files\Minecraft Server Manager\
├── MinecraftServerManager.exe          The panel (self-contained; includes the .NET runtime)
├── appsettings.json                    Default settings (don't edit; see below)
├── MinecraftServerManager.staticwebassets.endpoints.json
├── wwwroot\                            Web assets (styles, scripts, icons)
├── install-service.ps1 / uninstall-service.ps1
└── web.config                          Only used when hosted under IIS
```

## Panel data folder

Created by the installer and on first run. Kept when you upgrade or uninstall (the setup wizard asks whether to delete it when uninstalling).

```text
C:\ProgramData\Minecraft Server Manager\
├── appsettings.json                    Your start-up settings: address, port, HTTPS (see Configuration Reference)
└── data\
    ├── settings.json                   Panel settings and the admin password hash
    └── keys\                           Keys that protect sign-in cookies
```

`C:\ProgramData` is hidden by default; type the path into File Explorer's address bar to open it.

> **Back up this folder** along with your worlds. If `keys\` is lost, everyone simply has to sign in again; if `settings.json` is lost, you'll go through setup again (your worlds are unaffected).

## Zip installs

With the zip, everything is in the folder you extracted it to (for example `C:\MinecraftManager`): edit the `appsettings.json` there, and the panel data is in `data\` next to the executable.

## Windows registry

The installers record their choices under `HKEY_LOCAL_MACHINE\Software\Minecraft Server Manager` (install folder, version, panel port, which firewall rules they added) so upgrades and uninstalls can reuse them. The key is removed on uninstall.

## Server directory

```text
C:\MinecraftServer\
├── server.jar                  The Minecraft server installed by the Updates page
├── server.properties           Server settings (Server Properties page)
├── eula.txt                    eula=true once you've accepted the EULA
├── ops.json                    Operators          ┐
├── whitelist.json              Whitelist          │ Players page
├── banned-players.json         Banned players     │
├── banned-ips.json             Banned IPs         ┘
├── usercache.json              Name/UUID cache kept by Minecraft
├── <world>\                    One folder per world (e.g. survival\, creative-build\)
│   ├── level.dat               World settings: seed, version, game mode
│   ├── region\                 The terrain
│   ├── playerdata\             Player inventories and positions
│   └── session.lock            Held by the running server
├── logs\                       Minecraft logs (latest.log, older .log.gz)
├── crash-reports\              Created when Minecraft crashes
├── libraries\ versions\        Unpacked by the server jar on first start
├── backups\                    World backups (unless moved in Settings)
└── .manager\                   Panel working files
    ├── jars\                   The last 5 replaced server jars, for rollback
    └── tmp\                    Temporary files during world imports
```

## Naming conventions

| Item | Pattern | Example |
| --- | --- | --- |
| Backup | `<world>_<yyyy-MM-dd_HH-mm-ss>_<type>.zip` | `survival_2026-10-20_21-00-04_scheduled.zip` |
| Backup types | `manual`, `scheduled`, `preupdate`, `prerestore`, `predelete` | |
| Duplicate backup in the same second | `…_<type>_2.zip` | `survival_2026-10-20_21-00-04_manual_2.zip` |
| Archived jar | `<version>__<yyyyMMdd-HHmmss>.jar` (time it was replaced) | `1.21.10__20261020-221152.jar` |
| Restore in progress | `<world>.restore-old-<timestamp>` | Removed automatically when a restore succeeds |

Times are in the server's local time zone.

## Editing files by hand

- **While the server is stopped** you can edit `server.properties` and the JSON player lists by hand; the panel reads them fresh each time.
- **While the server is running**, Minecraft owns the player lists and will overwrite hand edits. Use the panel or console commands instead.
- After editing `server.properties` by hand while the page is open, click **Reload from disk** on [Server Properties](Server-Properties).
- You can copy a world folder into the server directory yourself. It appears on [Worlds](Worlds) as long as it contains `level.dat`.
