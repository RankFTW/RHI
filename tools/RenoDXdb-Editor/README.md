# RenoDXdb Editor

A WPF editor for the RHI mod databases hosted at [RankFTW/rhi-repo](https://github.com/RankFTW/rhi-repo/tree/main/database).

Supports both databases:
- **RenoDXdb.json** — named RenoDX addon mods (author, URLs, notes)
- **RenoDXdb-unreal.json** — Unreal Engine game entries (HDR method, upgrade format/size)

---

## Getting Started

Run `RenoDXdbEditor.exe` from the `publish\` folder. Keep the exe in the same folder as its companion DLLs — it will not work if moved on its own.

On first launch the editor automatically syncs both database files from GitHub and saves them next to the exe (`RenoDXdb.json` and `RenoDXdb-unreal.json`). You can then click either card to open and edit.

---

## GitHub Token (required for Push)

To push changes back to the repository you need a GitHub Personal Access Token.

1. Go to https://github.com/settings/tokens
2. Click **Generate new token (classic)**
3. Give it a name (e.g. `rhi-db-editor`)
4. Under **Scopes**, tick **repo** (full control of private repositories)
5. Click **Generate token** and copy it
6. In the editor, click **🔑 Token** in the toolbar and paste it in

The token is stored as `github_token.txt` next to the exe. Keep this file private — anyone with it can push to the repo.

---

## Toolbar

| Button | Action |
|--------|--------|
| **Open JSON** | Open any local `.json` file (DB type inferred from filename) |
| **Save** | Save the current file to disk |
| **Save As** | Save to a new path |
| **↻ Sync** | Re-download both DB files from GitHub and check for changes |
| **↑ Push** | Commit and push the current file to rhi-repo on GitHub |
| **🔑 Token** | Set or update your GitHub Personal Access Token |
| **◀ Change DB** | Return to the DB selector |
| **+ New** | Add a new entry (inserted alphabetically) |
| **Delete** | Delete the selected entry |
| Search box | Filter the list by name / author / comments |

---

## Named Mods (RenoDXdb.json)

Fields per entry:

| Field | Notes |
|-------|-------|
| Game Name | Required |
| Status | `Done` or `WIP` |
| Author | Mod author name |
| Snapshot URL (64-bit) | Direct link to `.addon64` file |
| Snapshot URL (32-bit) | Direct link to `.addon32` file (if applicable) |
| Nexus / GameBanana URL | Optional mod page link |
| Discord URL | Optional Discord link |
| Discussion URL | Optional GitHub Discussions link |
| Notes | Free-text notes |

---

## Unreal Games (RenoDXdb-unreal.json)

Fields per entry:

| Field | Notes |
|-------|-------|
| Game Name | Required |
| Status | `Done` or `WIP` |
| Method | `(none)`, `native`, `ini`, or `upgrade` |
| Upgrades | Two-column rows: **Format** + **Size** — see below |
| Comments | Free-text notes |

### Upgrades

The Upgrades field is used when Method is `upgrade`. Each row specifies one upgrade:

- **Format** — the DXGI format token (e.g. `B8G8R8A8_TYPELESS`, `R10G10B10A2_UNORM`)
- **Size** — the output size mode (`Output Size`, `Output Ratio`, `Any Size`)

Click **+ Add Upgrade Row** to add another line. Click **✕** to remove a row. Leave both columns as `(none)` if no upgrade is needed.

Example stored value: `` `B8G8R8A8_TYPELESS` `Output Size` ``

---

## Sync & Diff

When you click **↻ Sync** (or on startup), the editor downloads both files from GitHub. If the remote version differs from your local copy, a diff window appears showing added lines (green) and removed lines (red).

You can choose to **Overwrite Local** with the remote version, or **Cancel** to keep your local copy.

---

## Workflow

Typical edit session:

1. Launch the editor — it syncs automatically
2. Click the database you want to edit
3. If a diff is shown, review and decide whether to take the remote changes
4. Make your edits, click **Apply Changes** after each entry
5. Click **Save** when done
6. Click **↑ Push** to commit directly to rhi-repo

If you have unsaved changes and click Push, the editor will prompt you to save first.

---

## File Locations

All files are stored next to the exe:

| File | Purpose |
|------|---------|
| `RenoDXdb.json` | Local cache of the Named Mods database |
| `RenoDXdb-unreal.json` | Local cache of the Unreal database |
| `github_token.txt` | Your GitHub PAT (keep private) |
