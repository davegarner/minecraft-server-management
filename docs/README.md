# Documentation

| Path | Contents |
| --- | --- |
| [`wiki/`](wiki/) | Source of the [project wiki](https://gitea.uk.local:3001/Apps/minecraft-server-management/wiki): one Markdown file per page, `_Sidebar.md`/`_Footer.md`, and screenshots in `wiki/images/`. Start at [`wiki/Home.md`](wiki/Home.md). |
| [`Minecraft-Server-Manager-User-Guide.docx`](Minecraft-Server-Manager-User-Guide.docx) | The user guide as a Word document, generated from the wiki pages. |
| [`build/`](build/) | Generator for the Word document (`build-user-guide.mjs`), its introduction chapter and cover logo. |
| [`publish-wiki.sh`](publish-wiki.sh) | Pushes `wiki/` and the Word guide to the Gitea wiki repository. |

## Updating the documentation

1. Edit the Markdown in `wiki/`. Link to other pages by name (`[Backups](Backups)`) and to images as `images/<name>.png`.
2. Regenerate the Word guide:

   ```bash
   cd docs/build && npm install && node build-user-guide.mjs
   ```

   The chapters it includes, and their order, are listed in `CHAPTERS` at the top of the script. Wiki-only technical pages (Architecture, HTTP Endpoints, Development, etc.) are left out.

3. Publish the wiki:

   ```bash
   docs/publish-wiki.sh "Describe the change"
   ```

4. Commit and push. When the Gitea push mirror syncs the commit to GitHub, the `Publish wiki` workflow (`.github/workflows/publish-wiki.yml`) updates the [GitHub wiki](https://github.com/davegarner/minecraft-server-management/wiki) automatically, rewriting Gitea-only links to GitHub ones.

Make changes here rather than in either wiki's web editor; the next publish overwrites edits made there.

The server properties reference (`wiki/Server-Properties-Reference.md`) mirrors `src/MinecraftServerManager/Services/PropertySchema.cs`. Update it when the schema changes.
