# Development

How to build, run, test and release Minecraft Server Manager.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- Optional: Python 3 (for the Java stand-in below), a 64-bit Java runtime (to run a real Minecraft server)

Windows, macOS and Linux all work for development. Windows-only features (service, job object) are skipped elsewhere.

## Getting the code

```bash
git clone https://gitea.uk.local:3001/Apps/minecraft-server-management.git
cd minecraft-server-management
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
| `MachineConfiguration` | The `configure` command: defaults, port and remote-access changes, `--keep-existing`, keeping other keys, invalid arguments |

## Building a release

### Installers (Windows)

```powershell
.\scripts\build-release.ps1           # version from MinecraftServerManager.csproj
.\scripts\build-release.ps1 -Version 1.0.1
```

Needs Windows, the .NET 10 SDK and [Inno Setup 6.3+](https://jrsoftware.org/isdl.php). The WiX Toolset comes from NuGet. Output in `artifacts\release\`:

| File | Built from |
| --- | --- |
| `MinecraftServerManager-<version>-Setup.exe` | `installer/inno/MinecraftServerManager.iss` (Inno Setup) |
| `MinecraftServerManager-<version>-x64.msi` | `installer/wix/` (WiX v5 SDK project; `Package.wxs` plus a file list generated from the publish folder) |
| `MinecraftServerManager-<version>-win-x64.zip` | The publish folder |
| `SHA256SUMS.txt` | Checksums of the above |

Both installers install to `Program Files`, register the service, add firewall rules, and run `MinecraftServerManager.exe configure` to write `C:\ProgramData\Minecraft Server Manager\appsettings.json` (`Hosting/MachineConfiguration.cs`). The setup wizard runs `install-service.ps1`; the MSI uses `ServiceInstall` and the WiX Firewall extension, and remembers its options in the registry for upgrades and uninstalls. Keep the setup wizard's `AppId` and the MSI's `UpgradeCode` unchanged, or upgrades will install side by side.

`installer/tests/Test-Installers.ps1` installs, upgrades and uninstalls both packages on a disposable Windows machine and checks the service, firewall rules, settings and panel. **Don't run it on a machine you care about.**

### Portable zip only (any OS)

```powershell
.\scripts\publish.ps1                 # Windows
```

```bash
scripts/publish.sh                    # macOS / Linux, cross-compiles for Windows
```

Both produce `dist/MinecraftServerManager-win-x64/` and `dist/MinecraftServerManager-win-x64.zip`: a self-contained single-file `MinecraftServerManager.exe` (about 100 MB), `wwwroot`, `appsettings.json` and the service scripts. Pass another runtime identifier (e.g. `win-arm64`) as the argument to build for it.

### Release workflow

`.github/workflows/release.yml` runs on GitHub's Windows runners (it doesn't run on Gitea). On every push to `main` that touches the code or installers, it runs the tests, builds the release, builds a `99.0.0` MSI for the upgrade test, runs `Test-Installers.ps1`, and uploads the files as a workflow artifact.

To publish a release:

1. Set `<Version>` in `src/MinecraftServerManager/MinecraftServerManager.csproj` and add the changes to [Release Notes](Release-Notes).
2. Commit, then tag and push: `git tag -a v1.0.1 -m "Version 1.0.1" && git push origin v1.0.1`.
3. Sync the GitHub mirror (Gitea → *Settings → Repository → Mirror Settings → Synchronize Now*), or wait for the next scheduled sync.
4. The workflow checks the tag matches the project version, runs the tests and installer checks, and publishes a GitHub release with the installers. The release text comes from `installer/RELEASE_NOTES.md`.
5. On Gitea, create a release for the same tag and attach the same files.

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

The repository is push-mirrored to GitHub. There, the `Publish wiki` workflow (`.github/workflows/publish-wiki.yml`) copies `docs/wiki` into the GitHub wiki whenever it changes on `main`. It pushes to `master` (the only branch GitHub wikis display) and rewrites links to the LAN Gitea server so they point at GitHub. It doesn't run on Gitea. Edits made directly in either wiki's web editor are overwritten by the next publish, so change `docs/wiki` instead.

The Word user guide (`docs/Minecraft-Server-Manager-User-Guide.docx`) is generated from the same Markdown pages:

```bash
cd docs/build && npm install && node build-user-guide.mjs
```

Screenshots in `docs/wiki/images/` were captured from a demo instance driven by the fake server.
