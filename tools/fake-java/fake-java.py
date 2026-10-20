#!/usr/bin/env python3
"""
Development stand-in for `java -jar server.jar nogui`.

Lets you exercise Minecraft Server Manager without installing Java or running a real server.
Point Settings -> Java executable at this script (or fake-java.cmd on Windows). It:
  * answers `-version` like a Java 21 runtime,
  * reads the Minecraft version from server.jar's version.json,
  * prints vanilla-style startup logs, creates the world folder with a real (gzipped NBT) level.dat,
  * responds to stop, save-all, save-off/on, list, op, say, kick,
  * plus test-only commands: fakejoin <name>, fakeleave <name>, fakechat <name> <text>, fakewarn <text>,
    fakeadvancement <name> <title>, crash (exits with code 1).
Set MCM_FAKE_LOAD=<megabytes> in the manager's environment to simulate memory and CPU use.
"""
import datetime
import gzip
import json
import os
import random
import struct
import sys
import time
import uuid
import zipfile


def ts():
    return datetime.datetime.now().strftime("%H:%M:%S")


def log(message, level="INFO", thread="Server thread"):
    print(f"[{ts()}] [{thread}/{level}]: {message}", flush=True)


if "-version" in sys.argv[1:]:
    sys.stderr.write('openjdk version "21.0.5" 2024-10-15 LTS\n'
                     'OpenJDK Runtime Environment Temurin-21.0.5+11 (build 21.0.5+11-LTS)\n'
                     'OpenJDK 64-Bit Server VM Temurin-21.0.5+11 (build 21.0.5+11-LTS, mixed mode, sharing)\n')
    sys.exit(0)


def jar_version():
    jar = sys.argv[sys.argv.index("-jar") + 1] if "-jar" in sys.argv else "server.jar"
    try:
        with zipfile.ZipFile(jar) as z:
            return json.loads(z.read("version.json"))["name"]
    except (OSError, KeyError, ValueError, zipfile.BadZipFile):
        return "1.21.11"


# --- minimal NBT writer for level.dat ---------------------------------------------------------
def nbt_string(s):
    data = s.encode("utf-8")
    return struct.pack(">H", len(data)) + data


def tag(tag_type, name, payload):
    return bytes([tag_type]) + nbt_string(name) + payload


def compound(*items):
    return b"".join(items) + b"\x00"


def write_level_dat(path, name, seed, version, game_type, hardcore):
    data = compound(
        tag(8, "LevelName", nbt_string(name)),
        tag(10, "Version", compound(tag(8, "Name", nbt_string(version)))),
        tag(10, "WorldGenSettings", compound(tag(4, "seed", struct.pack(">q", seed)))),
        tag(3, "GameType", struct.pack(">i", game_type)),
        tag(1, "hardcore", b"\x01" if hardcore else b"\x00"),
        tag(4, "LastPlayed", struct.pack(">q", int(time.time() * 1000))),
    )
    with gzip.open(path, "wb") as f:
        f.write(tag(10, "", compound(tag(10, "Data", data))))


def read_properties():
    props = {}
    if os.path.exists("server.properties"):
        with open("server.properties", encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith(("#", "!")):
                    key, _, value = line.partition("=")
                    props[key.strip()] = value.strip().replace("\\:", ":")
    return props


props = read_properties()
version = jar_version()
eula = open("eula.txt").read() if os.path.exists("eula.txt") else ""
if "eula=true" not in eula:
    log("You need to agree to the EULA in order to run the server. Go to eula.txt for more info.", thread="ServerMain")
    sys.exit(0)

level = props.get("level-name") or "world"
game_modes = {"survival": 0, "creative": 1, "adventure": 2, "spectator": 3}
log("Environment: Environment[sessionHost=https://sessionserver.mojang.com, servicesHost=https://api.minecraftservices.com, name=PROD]", thread="ServerMain")
log("Loaded 1407 recipes", thread="ServerMain")
log("Loaded 1520 advancements", thread="ServerMain")
log(f"Starting minecraft server version {version}")
log("Loading properties")
log(f"Default game type: {props.get('gamemode', 'survival').upper()}")
log("Generating keypair")
log(f"Starting Minecraft server on *:{props.get('server-port', '25565')}")
log("Using default channel type")
log(f'Preparing level "{level}"')

if not os.path.isdir(level):
    seed_text = props.get("level-seed", "")
    try:
        seed = int(seed_text) if seed_text else random.randint(-2**63, 2**63 - 1)
    except ValueError:
        seed = hash(seed_text) & 0x7FFFFFFFFFFFFFFF
    os.makedirs(os.path.join(level, "region"))
    os.makedirs(os.path.join(level, "playerdata"))
    for x in (-1, 0):
        for z in (-1, 0):
            with open(os.path.join(level, "region", f"r.{x}.{z}.mca"), "wb") as f:
                f.write(os.urandom(512 * 1024))
    write_level_dat(os.path.join(level, "level.dat"), level, seed, version,
                    game_modes.get(props.get("gamemode", "survival"), 0), props.get("hardcore") == "true")
    log("Preparing start region for dimension minecraft:overworld")
    for pct in (0, 18, 47, 83, 100):
        log(f"Preparing spawn area: {pct}%", thread="Worker-Main-2")
        time.sleep(0.15)
with open(os.path.join(level, "session.lock"), "w", encoding="utf-8") as f:
    f.write("☃")
log("Time elapsed: 2731 ms")
log('Done (3.412s)! For help, type "help"')

# Optional: MCM_FAKE_LOAD=<megabytes> holds that much memory and keeps a core lightly busy,
# so the dashboard's CPU and memory meters show realistic-looking values.
if os.environ.get("MCM_FAKE_LOAD", "").isdigit():
    import threading
    ballast = os.urandom(int(os.environ["MCM_FAKE_LOAD"]) * 1024 * 1024)  # incompressible, so it stays resident

    def busy():
        while True:
            for page in range(0, len(ballast), 4096):  # touch every page so the OS keeps it resident
                ballast[page]
            time.sleep(0.2)

    threading.Thread(target=busy, daemon=True).start()

players = []
for raw in sys.stdin:
    command = raw.strip().lstrip("/")
    if not command:
        continue
    word, _, rest = command.partition(" ")
    if word == "stop":
        log("Stopping the server")
        log("Stopping server")
        log("Saving players")
        log("Saving worlds")
        log(f"Saving chunks for level 'ServerLevel[{level}]'/minecraft:overworld")
        time.sleep(0.3)
        log("ThreadedAnvilChunkStorage: All dimensions are saved")
        sys.exit(0)
    elif word == "save-all":
        log("Saving the game (this may take a moment!)")
        time.sleep(0.3)
        log("Saved the game")
    elif word == "save-off":
        log("Automatic saving is now disabled")
    elif word == "save-on":
        log("Automatic saving is now enabled")
    elif word == "list":
        log(f"There are {len(players)} of a max of {props.get('max-players', '20')} players online: {', '.join(players)}")
    elif word == "fakejoin":
        players.append(rest)
        log(f"{rest}[/192.168.1.{random.randint(20, 90)}:{random.randint(50000, 60000)}] logged in with entity id {random.randint(100, 999)} at (12.5, 71.0, -34.5)")
        log(f"{rest} joined the game")
    elif word == "fakeleave":
        if rest in players:
            players.remove(rest)
        log(f"{rest} lost connection: Disconnected")
        log(f"{rest} left the game")
    elif word == "fakechat":
        speaker, _, text = rest.partition(" ")
        log(f"<{speaker}> {text}")
    elif word == "fakeadvancement":
        name, _, title = rest.partition(" ")
        log(f"{name} has made the advancement [{title}]")
    elif word == "fakewarn":
        log(rest, level="WARN")
    elif word == "crash":
        log("Encountered an unexpected exception", level="ERROR")
        log("java.lang.IllegalStateException: Simulated crash for testing", level="ERROR")
        sys.exit(1)
    elif word == "say":
        log(f"[Server] {rest}")
    elif word == "op":
        ops = json.load(open("ops.json")) if os.path.exists("ops.json") else []
        ops.append({"uuid": str(uuid.uuid4()), "name": rest, "level": 4, "bypassesPlayerLimit": False})
        with open("ops.json", "w") as f:
            json.dump(ops, f, indent=2)
        log(f"Made {rest} a server operator")
    elif word == "kick":
        name = rest.split(" ")[0]
        if name in players:
            players.remove(name)
            log(f"Kicked {name}: Kicked by an operator")
            log(f"{name} left the game")
    elif word in ("time", "weather", "whitelist", "tick", "difficulty", "gamerule"):
        log(f"Ran /{command}")
    else:
        log("Unknown or incomplete command. See below for error")
