# HTTP Endpoints

Most of the panel runs over the Blazor live connection, but a few plain HTTP endpoints exist for sign-in, file transfer and sign-out. All of them are defined in `Program.cs`, `Endpoints/ManagerEndpoints.cs` and the `Components/Account` pages.

## Pages and forms

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/setup` | Anonymous | First-run setup form. Redirects to `/login` once an account exists. |
| POST | `/setup` | Anonymous + anti-forgery | Creates the admin account, sets the server directory, signs in, redirects to `/`. |
| GET | `/login?ReturnUrl=…` | Anonymous | Sign-in form. Redirects to `/setup` if no account exists. |
| POST | `/login` | Anonymous + anti-forgery | Validates credentials (with lockout), sets the `mcm.auth` cookie, redirects to `ReturnUrl` (local URLs only) or `/`. |
| POST | `/logout` | Anti-forgery | Clears the cookie, redirects to `/login`. |
| GET | `/`, `/console`, `/properties`, `/worlds`, `/backups`, `/updates`, `/players`, `/settings` | Signed in | Interactive pages. Unauthenticated requests get `302` to `/login?ReturnUrl=…`. |
| GET | `/not-found`, `/error` | Anonymous | Status-code and error pages. |
| — | `/_blazor` | Signed-in circuit | SignalR hub (WebSockets, with fallbacks) used by the interactive pages. |

## API

### Download a backup

```http
GET /api/backups/{fileName}
```

| | |
| --- | --- |
| Auth | Signed in (cookie) |
| Parameters | `fileName`: a backup file name exactly as listed, e.g. `survival_2026-10-20_21-00-04_scheduled.zip`. Must be a plain file name ending in `.zip`. |
| Responses | `200` `application/zip` with `Content-Disposition: attachment` (supports range requests); `404` if the name is invalid or the file doesn't exist; `302` to `/login` if not signed in. |

### Import a world

```http
POST /api/worlds/import
Content-Type: multipart/form-data
```

| Field | Required | Description |
| --- | --- | --- |
| `__RequestVerificationToken` | Yes | Anti-forgery token (rendered by the import dialog). |
| `file` | Yes | Zip archive containing a Java Edition world (a folder with `level.dat`, at any depth). |
| `name` | No | Target world name; defaults to the sanitised file name. |
| `makeActive` | No | `true` to set the imported world as `level-name`. |

| | |
| --- | --- |
| Auth | Signed in (cookie) |
| Limits | Request body up to 8 GB. |
| Responses | `302` to `/worlds?imported=<name>` on success, or `/worlds?importError=<message>` on a validation or extraction failure; `400` if the anti-forgery token is missing or invalid. |

The import first saves the upload under `<server>/.manager/tmp`, extracts the folder containing the shallowest `level.dat` into a staging folder (rejecting unsafe paths and skipping `session.lock`), then moves it into place.

## Outbound requests

| URL | Used for |
| --- | --- |
| `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` | Version list (cached 15 minutes) |
| Version JSON URLs from the manifest | Server download URL, size, SHA-1 and Java requirement |
| `https://piston-data.mojang.com/…/server.jar` | Server downloads |
| `https://api.mojang.com/users/profiles/minecraft/{name}` | Player UUID lookup when editing lists while the server is stopped |

All use the named `HttpClient` `"mojang"` with the user agent `MinecraftServerManager/1.0` and a 10-minute timeout.
