# Files and Folders

Where the panel and the Minecraft server keep everything, assuming the defaults (`C:\MinecraftManager` for the panel and `C:\MinecraftServer` for Minecraft).

## Panel folder

```text
C:\MinecraftManager\
├── MinecraftServerManager.exe          The panel (self-contained; includes the .NET runtime)
├── appsettings.json                    Start-up configuration (see Configuration Reference)
├── MinecraftServerManager.staticwebassets.endpoints.json
├── wwwroot\                            Web assets (styles, scripts, icons)
├── install-service.ps1 / uninstall-service.ps1
├── web.config                          Only used when hosted under IIS
└── data\                               Panel data (created on first run)
    ├── settings.json                   Panel settings and the admin password hash
    └── keys\                           Keys that protect sign-in cookies
```

> **Back up the `data` folder** along with your worlds. If `keys\` is lost, everyone simply has to sign in again; if `settings.json` is lost, you'll go through setup again (your worlds are unaffected).

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
