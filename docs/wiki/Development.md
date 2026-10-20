# Development

How to build, run, test and release Minecraft Server Manager.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- Optional: Python 3 (for the Java stand-in below), a 64-bit Java runtime (to run a real Minecraft server)

Windows, macOS and Linux all work for development. Windows-only features (service, job object) are skipped elsewhere.

## Getting the code

```bash
git clone https://gitea.uk.local:3001/Apps/Minecraft-Server.git
cd Minecraft-Server
```

## Running locally

```bash
dotnet run --project src/MinecraftServerManager
```

This uses the `http` launch profile: Development environment, <http://localhost:5031>. Panel data goes to `src/MinecraftServerManager/data/` (ignored by Git). To use a separate data folder or port:

```bash
dotnet run --project src/MinecraftServerManager -- --urls http://localhost:5099 --Manager:DataDirectory=/tmp/mcm-data
```

> Run either with `dotnet run` (Development) or from a **published** build. Running the raw `bin/` output in the Production environment doesn't serve framework static files such as `blazor.web.js`.

## Testing without Java: the fake server

`tools/fake-java/fake-java.py` imitates `java -jar server.jar nogui` closely enough to exercise the whole panel:

- answers `-version` as Java 21 and reads the Minecraft version from `server.jar`,
- prints vanilla-style start-up logs and creates a world (real gzip NBT `level.dat`) on first start,
- responds to `stop`, `save-all`, `save-off`/`save-on`, `list`, `op`, `say`, `kick`,
- adds test commands: `fakejoin <name>`, `fakeleave <name>`, `fakechat <name> <text>`, `fakeadvancement <name> <title>`, `fakewarn <text>`, and `crash` (exits with code 1).

Point **Settings → Java executable** at the script (`fake-java.cmd` on Windows). Set the environment variable `MCM_FAKE_LOAD=<megabytes>` before starting the panel to make the stub hold memory and use some CPU, so the Dashboard meters show realistic values.

You still need a `server.jar` (install one through the Updates page; the stub only reads its version) and `eula.txt`.

## Tests

```bash
dotnet test
```

The xUnit project `tests/MinecraftServerManager.Tests` covers:

| Area | Examples |
| --- | --- |
| `JavaPropertiesDocument` | Separators, escapes, `\uXXXX`, continuations, round-tripping with comments, Latin-1 fallback |
| Helpers | Java version parsing, command-line splitting, launch arguments, world-name rules, offline UUIDs, password hashing, property validation, invariant CSS formatting |
| Worlds and backups (integration, temp folders) | `level.dat` reading, dimension folders, world creation, backup contents, retention, restore and rollback, path-traversal rejection, world import, delete with backup, EULA |

## Building a release

```powershell
.\scripts\publish.ps1                 # Windows
```

```bash
scripts/publish.sh                    # macOS / Linux, cross-compiles for Windows
```

Both produce `dist/MinecraftServerManager-win-x64/` and `dist/MinecraftServerManager-win-x64.zip`: a self-contained single-file `MinecraftServerManager.exe` (about 100 MB), `wwwroot`, `appsettings.json` and the service scripts. Pass another runtime identifier (e.g. `win-arm64`) as the argument to build for it.

## Conventions

- **Services** are singletons that own state and raise events; components only orchestrate and render.
- **UI updates** from background threads go through `InvokeAsync(StateHasChanged)`; always unsubscribe in `Dispose`.
- **Long operations** (backup, restore, install) run in the service and publish immutable progress records, so they continue if the user navigates away.
- **Destructive actions** use `ConfirmDialog` (optionally with a checkbox or type-to-confirm). Feedback uses `ToastService`.
- **File writes** that must not be half-written use `FileUtil.WriteAllTextAtomic`.
- **CSS** uses the tokens defined at the top of `wwwroot/app.css`; dark is the default, with light under `prefers-color-scheme: light`.
- Page components are named `*Page.razor` where the plain name would clash with an injected service property.

### Adding a server property to the editor

Add a `PropertyDefinition` to `PropertySchema.All` with its category, kind, default, description and (for numbers) range. Unknown keys already work as plain text, so this only improves the editor. Regenerate the [Server Properties Reference](Server-Properties-Reference) afterwards.

### Adding a page

1. Create `Components/Pages/<Name>Page.razor` with `@page "/route"`. It's protected by `[Authorize]` automatically.
2. Add a `NavLink` to `Components/Layout/MainLayout.razor` (and an icon to `Shared/Icon.razor` if needed).

## Documentation

The wiki is kept in the main repository under `docs/wiki/` and published to the Gitea wiki:

```bash
docs/publish-wiki.sh            # pushes docs/wiki to the repository's .wiki.git
```

The Word user guide (`docs/Minecraft-Server-Manager-User-Guide.docx`) is generated from the same Markdown pages:

```bash
cd docs/build && npm install && node build-user-guide.mjs
```

Screenshots in `docs/wiki/images/` were captured from a demo instance driven by the fake server.
