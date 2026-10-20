# Introduction

**Minecraft Server Manager** is a web control panel for running a **Minecraft: Java Edition** dedicated server on **Windows Server**. It installs as a single program, runs in the background as a Windows service, and is managed from any web browser, on the server itself or (if you allow it) from another computer.

![The dashboard](images/dashboard.png)

## What you can do with it

| Area | What it does |
| --- | --- |
| **Run the server** | Start, stop and restart with one click. Watch status, players, CPU and memory live. The server restarts itself after a crash and shuts down cleanly with Windows. |
| **Console** | See the live server log, colour-coded, and type commands without logging on to the server. |
| **Configure** | Edit every `server.properties` setting through a form with descriptions and validation. |
| **Worlds** | Generate new worlds (with seeds, world types and superflat presets), switch between worlds, and import singleplayer saves. |
| **Backups** | Take safe backups of a running world, schedule them, and restore or download them. |
| **Updates** | Install or update the official Minecraft server from Mojang, verified and with automatic backup and rollback. |
| **Players** | Manage operators, the whitelist and bans, and kick or ban players who are online. |

## About this guide

This guide is for the people who look after the Minecraft server: installing the panel, running the server day to day, and fixing problems. No programming knowledge is needed; some familiarity with Windows Server (PowerShell, Services, Windows Firewall) helps for installation.

| Chapter | Covers |
| --- | --- |
| Quick Start and Installation | Getting from the download to a running server |
| User Guide to Settings | Each page of the panel, in the order they appear in the menu |
| Running as a Windows Service, Remote Access and HTTPS | Setting the panel up for everyday, unattended use |
| Troubleshooting and FAQ | Solving common problems |
| Appendices | Every server setting, and where the panel keeps its files |

**Conventions.** Names of buttons, fields and pages are shown in **bold**. File names, settings keys and commands are shown in `code style`. Screenshots come from a demonstration server, so names, sizes and dates will differ from yours.

This guide is also available online in the project wiki, which includes additional technical reference material for developers.
