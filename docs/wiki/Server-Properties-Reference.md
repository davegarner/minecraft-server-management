# Server Properties Reference

Every `server.properties` key the panel recognises, grouped the way the [Server Properties](Server-Properties) page groups them. This table is generated from the panel's property schema (`Services/PropertySchema.cs`).

Keys that aren't listed here (added by newer Minecraft versions or by mods) still appear in the editor under **Other** and can be edited as plain text. Most changes take effect the next time the server starts.

## General

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `motd` | Text | `A Minecraft Server` | Message shown under the server name in the multiplayer server list. Supports § formatting codes. |
| `server-port` | Number (1 to 65535) | `25565` | TCP port the server listens on. Allow it through Windows Firewall so players can connect. |
| `server-ip` | Text | *(blank)* | Bind to a specific local IP address. Leave blank to listen on all network interfaces. |
| `max-players` | Number (0 to 2147483647) | `20` | Maximum number of players that can be online at the same time. |
| `enable-status` | true / false | `true` | Respond to server list pings. When disabled the server appears offline, but players can still join. |
| `hide-online-players` | true / false | `false` | Hide the list of online players from server list status responses. |
| `bug-report-link` | Text | *(blank)* | URL shown as the 'Report Server Bugs' link on the disconnect screen. |
| `accepts-transfers` | true / false | `false` | Accept players transferred from another server with the /transfer command. |

## Gameplay

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `gamemode` | One of: `survival`, `creative`, `adventure`, `spectator` | `survival` | Default game mode for players joining for the first time. |
| `force-gamemode` | true / false | `false` | Put players into the default game mode every time they join. |
| `difficulty` | One of: `peaceful`, `easy`, `normal`, `hard` | `easy` | Difficulty of the world: affects mob damage, hunger and more. |
| `hardcore` | true / false | `false` | Players are switched to spectator mode when they die, and difficulty is locked to hard. |
| `pvp` | true / false | `true` | Allow players to damage each other. |
| `allow-flight` | true / false | `false` | Allow flight in survival mode (e.g. with mods). When false, players airborne for 5 seconds are kicked. |
| `allow-nether` | true / false | `true` | Allow players to travel to the Nether. |
| `spawn-monsters` | true / false | `true` | Spawn hostile mobs. |
| `spawn-protection` | Number (0 to 2147483647) | `16` | Radius in blocks around world spawn that only operators can change. 0 disables protection. |
| `enable-command-block` | true / false | `false` | Allow command blocks to run commands. |
| `player-idle-timeout` | Number (0 to 2147483647) | `0` | Minutes a player can be idle before being kicked. 0 disables. |
| `max-world-size` | Number (1 to 29999984) | `29999984` | Maximum radius of the world border, in blocks. |

## World

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `level-name` | Text | `world` | Folder name of the world the server loads. Use the Worlds page to switch or create worlds. |
| `level-seed` | Text | *(blank)* | Seed used when a new world is generated. Blank means random. Has no effect on existing worlds. |
| `generator-settings` | JSON | `{}` | JSON settings used to customise newly generated worlds, such as superflat layers. |
| `generate-structures` | true / false | `true` | Generate structures such as villages, temples and strongholds in new chunks. |
| `initial-enabled-packs` | Text | `vanilla` | Comma-separated data packs enabled when a new world is created. |
| `initial-disabled-packs` | Text | *(blank)* | Comma-separated data packs that are not enabled automatically when a new world is created. |

## Players & Security

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `online-mode` | true / false | `true` | Verify that players own a Minecraft account. Only disable on private LANs or behind a proxy, because anyone can then join with any name. |
| `white-list` | true / false | `false` | Only allow players on the whitelist to join. |
| `enforce-whitelist` | true / false | `false` | Kick online players who are not on the whitelist when it is reloaded. |
| `enforce-secure-profile` | true / false | `true` | Require players to have a Mojang-signed public key in order to join. |
| `prevent-proxy-connections` | true / false | `false` | Kick players whose connection appears to come through a proxy or VPN. |
| `op-permission-level` | Number (0 to 4) | `4` | Default permission level given by /op (1 = bypass spawn protection, 4 = all commands). |
| `function-permission-level` | Number (1 to 4) | `2` | Permission level that data pack functions run with. |
| `broadcast-console-to-ops` | true / false | `true` | Show console command output to online operators. |
| `broadcast-rcon-to-ops` | true / false | `true` | Show RCON command output to online operators. |
| `log-ips` | true / false | `true` | Write player IP addresses to the server log when they connect. |

## Performance

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `view-distance` | Number (3 to 32) | `10` | Radius of chunks (3-32) the server sends to players. Lower values reduce CPU, RAM and bandwidth. |
| `simulation-distance` | Number (3 to 32) | `10` | Radius of chunks (3-32) around players in which mobs, crops and redstone are updated. |
| `entity-broadcast-range-percentage` | Number (10 to 1000) | `100` | How close entities must be before they're sent to clients, as a percentage of the default. |
| `network-compression-threshold` | Number (-1 to 2147483647) | `256` | Packets larger than this many bytes are compressed. -1 disables compression. |
| `max-tick-time` | Number (-1 or more) | `60000` | Milliseconds a single tick may take before the watchdog shuts the server down. -1 disables the watchdog. |
| `sync-chunk-writes` | true / false | `true` | Write chunk files synchronously. Safer against data loss, but slower. |
| `region-file-compression` | One of: `deflate`, `lz4`, `none` | `deflate` | Compression used for region files. |
| `rate-limit` | Number (0 to 2147483647) | `0` | Maximum packets per second a player may send before being kicked. 0 disables. |
| `max-chained-neighbor-updates` | Number | `1000000` | Limit on consecutive neighbour updates before further updates are skipped. Negative disables the limit. |
| `pause-when-empty-seconds` | Number | `60` | Seconds without any players online before the server pauses ticking. 0 or negative disables pausing. |
| `use-native-transport` | true / false | `true` | Use Linux-optimised networking. Has no effect on Windows. |

## Resource Pack

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `resource-pack` | Text | *(blank)* | Direct download URL of a resource pack players are prompted to use. |
| `resource-pack-sha1` | Text | *(blank)* | SHA-1 hash of the resource pack, used to verify downloads. |
| `resource-pack-id` | Text | *(blank)* | Optional UUID identifying the resource pack. |
| `resource-pack-prompt` | Text | *(blank)* | Custom message (JSON text) shown when players are prompted to download the pack. |
| `require-resource-pack` | true / false | `false` | Disconnect players who decline the resource pack. |

## RCON & Query

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `enable-rcon` | true / false | `false` | Enable remote console access over the RCON protocol. Do not expose RCON to the internet. |
| `rcon.port` | Number (1 to 65535) | `25575` | TCP port for RCON. |
| `rcon.password` | Secret text | *(blank)* | Password for RCON. Use a long, random value. |
| `enable-query` | true / false | `false` | Enable the GameSpy4 query protocol used by server listing sites. |
| `query.port` | Number (1 to 65535) | `25565` | UDP port for the query protocol. |

## Advanced

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `enable-jmx-monitoring` | true / false | `false` | Expose tick time metrics through JMX (requires extra JVM flags). |
| `text-filtering-config` | Text | *(blank)* | Configuration for an external chat text filtering service. |
| `text-filtering-version` | Number (0 to 2147483647) | `0` | Version of the text filtering configuration format. |

## Notes

- **Escaping.** The panel writes the file the way Java does: `:` `=` `#` `!` are escaped with `\`, and non-ASCII characters (such as the `§` colour sign in a MOTD) are written as `\uXXXX` escapes. Minecraft reads these back correctly.
- **World settings** (`level-seed`, `level-type`, `generator-settings`, `generate-structures`) only apply when a world is first generated. Use [Worlds](Worlds) to create a new world with different settings.
- **Version differences.** Mojang occasionally adds, renames or removes keys between versions. The server rewrites `server.properties` on start-up with the keys it supports.
