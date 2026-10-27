# RenoDXdb Editor

A WPF editor for the RHI mod databases hosted at [RankFTW/rhi-repo](https://github.com/RankFTW/rhi-repo/tree/main/database).

Supports three databases:
- **RenoDXdb.json** — named RenoDX addon mods (author, URLs, notes)
- **RenoDXdb-unreal.json** — Unreal Engine game entries (HDR method, upgrade format/size)
- **RenoDXdb-unity.json** — Unity Engine game entries (upgrade keys, settings)

---

## Getting Started

Run `RenoDXdbEditor.exe` from the `publish\` folder. Keep the exe in the same folder as its companion DLLs — it will not work if moved on its own.

On first launch the editor automatically syncs all three database files from GitHub and saves them next to the exe. Click any of the three cards on the selector screen to open and edit that database.

---

## GitHub Token (required for Push and Wiki Check)

To push changes or fetch the RenoDX wiki you need a GitHub Personal Access Token with `repo` scope.

1. Go to https://github.com/settings/tokens
2. Click **Generate new token (classic)**
3. Give it a name (e.g. `rhi-db-editor`)
4. Under **Scopes**, tick **repo** (full control of private repositories)
5. Click **Generate token** and copy it
6. In the editor, click **🔑 Token** in the toolbar and paste it in

The token is stored as `github_token.txt` next to the exe. Keep this file private.

---

## Toolbar

| Button | Action |
|--------|--------|
| **Open JSON** | Open any local `.json` file (DB type inferred from filename) |
| **Save** | Save the current file to disk |
| **Save As** | Save to a new path |
| **↻ Sync** | Re-download all three DB files from GitHub and check for changes |
| **↑ Push** | Commit and push the current file to rhi-repo on GitHub |
| **🔑 Token** | Set or update your GitHub Personal Access Token |
| **◀ Change DB** | Return to the DB selector screen |
| **+ New** | Add a new entry (inserted alphabetically) |
| **Delete** | Delete the selected entry |
| **Wiki Check** | Scrape the RenoDX wiki and find entries not yet in the current DB (see below) |
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
| Upgrades | One or more rows: **Format** + **Size** — see below |
| Comments | Free-text notes |

### Upgrades (Unreal)

Used when Method is `upgrade`. Each row is a DXGI format token paired with an output size mode:

- **Format** — e.g. `B8G8R8A8_TYPELESS`, `R10G10B10A2_UNORM`, `Upgrade_CopyDestinations`, `Upgrade_UseSCRGB`
- **Size** — `Output Size`, `Output Ratio`, `Any Size`, or a numeric value (`0`, `1`, `2`)

Click **+ Add Upgrade Row** to add a line. Click **✕** to remove a row.

---

## Unity Games (RenoDXdb-unity.json)

Same structure as Unreal entries but without the Method field — Unity entries only use Upgrades and Comments.

Fields per entry:

| Field | Notes |
|-------|-------|
| Game Name | Required |
| Status | `Done` or `WIP` |
| Upgrades | One or more key/value pairs — see below |
| Comments | Free-text notes |

### Upgrades (Unity)

Unity entries use `Upgrade_*` prefixed keys and boolean/numeric settings rather than DXGI format names. Common keys:

- `Upgrade_R8G8B8A8_UNORM`, `Upgrade_R10G10B10A2_UNORM`, `Upgrade_R16G16B16A16_FLOAT`, etc.
- `Upgrade_CopyDestinations`, `Upgrade_UseSCRGB`, `Upgrade_SwapChainCompatibility`
- `Use_Swapchain_Proxy`, `Use_Resource_Cloning`, `Force_Pipeline_Cloning`
- `SettingsMode`, `Blit_Copy_Hack`, `Proxy_Revert_State`, `Tonemap_Offset`, `Scaling_Offset`

Values are typically `0`/`1`/`2` (Off/On/Mode), `Output Size`, `Output Ratio`, or `Any Size`.

---

## Wiki Check

The **Wiki Check** button scrapes the RenoDX wiki and finds entries not yet in the current database.

- **Named Mods DB** — finds completed named mods from the wiki mods table
- **Unreal DB** — finds completed UE-Extended games from the wiki UE table
- **Unity DB** — finds completed Unity games from the wiki Unity table

After fetching, a review window opens listing new entries. Check or uncheck each one, then click **Accept Selected** to add them to the DB. Click **Save** afterwards to persist, then **Push** to publish.

---

## Sync & Diff

When you click **↻ Sync** (or on startup), the editor downloads all three files from GitHub. If the remote version differs from your local copy, a diff window appears showing added lines (green) and removed lines (red).

Choose **Overwrite Local** to take the remote version, or **Cancel** to keep your local copy. If a diffed file is currently open it will be reloaded automatically.

---

## Workflow

Typical edit session:

1. Launch the editor — it syncs automatically
2. Click the database you want to edit
3. If a diff is shown, review and decide whether to take the remote changes
4. Make your edits, click **Apply Changes** after each entry
5. Optionally click **Wiki Check** to find new entries from the wiki
6. Click **Save** when done
7. Click **↑ Push** to commit directly to rhi-repo (shows a preview diff before pushing)

If you have unsaved changes and click Push, the editor will prompt you to save first.

---

## File Locations

All files are stored next to the exe:

| File | Purpose |
|------|---------|
| `RenoDXdb.json` | Local cache of the Named Mods database |
| `RenoDXdb-unreal.json` | Local cache of the Unreal/UE-Extended database |
| `RenoDXdb-unity.json` | Local cache of the Unity database |
| `github_token.txt` | Your GitHub PAT (keep private) |
