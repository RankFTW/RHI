# Custom OptiScaler builds: research and plan

Researched 7 Nov 2026 for issue #163 (a user wants to point RHI at any OptiScaler fork or import their own build). Sources: a survey of RHI's code (every place the four built-in versions are handled) and `research_notes/Custom OptiScaler builds/landscape.md` (forks, archive layouts, other tools, GitHub API details, with links).

## What exists today

### In RHI
- Four built-in versions: Stable, Nightly, DlssNr, F5. There is no shared abstraction. Each has its own copy-pasted staging block in `OptiScalerService.Staging.cs`, its own fields (`IsStagingReady*`, `HasUpdate*`, `StagedVersion*`) and 4-way ternaries or switches in about 20 files:
  - install, update and uninstall
  - INI template choice
  - update check and Update All grouping
  - version display (BuildCards / CacheLoad)
  - startup staging
  - the cog dropdown and preset label maps
  - Default version validation
  - Extras links
  - Update History colours
- The four differ in five ways:

  | Difference | Options in use |
  |---|---|
  | GitHub API shape | releases list or `/latest` |
  | Tag cleanup | strip `nightly-`, strip `v`, or none |
  | Asset rule | first `.7z`, first `.zip`, or a name filter |
  | Extractor | 7-Zip or `ZipFile` |
  | Clearing staging | top-level files only (Stable) or everything |

  Everything finds the payload as "the folder that contains `OptiScaler.dll`".
- "OptiScaler 10" builds (an `OptiScaler\` subfolder) are detected inconsistently:
  - by version name in install (MFG plugin hook), in the cog's advanced sections and in Update All's Streamline step;
  - by folder layout in `GetPluginsDir` and `IsOptiScalerPluginHost`.
- Code to reuse:
  - `GitHubETagCache` and `GitHubTokenHandler`;
  - `AddonPackService.ResolveDownloadUrlFromApiAsync` (handles both list and `/latest` responses);
  - the ReShade "Custom" channel (a folder of user files, a per-game choice, hash-based change detection);
  - the Custom game folders Settings card (a user-edited list, add/remove, folder picker);
  - 7-Zip extraction from drag-drop.

### F5 gaps found along the way (1-4 fixed 7 Nov 2026; 5 left for Phase 0)
1. **Version text:** the card's OptiScaler version falls back to Stable's for F5 when `rhi_install.txt` has no version (`MainViewModel.BuildCards.cs` ~L1184, `CacheLoad.cs` ~L679).
2. **Startup:** F5 has no startup staging task in either init path (`Init.cs`, `BackgroundScan.cs`).
3. **Post-install setup:** fresh installs skip the Streamline / DLSS Enabler / frame generation step for F5. `InstallEventHandler.cs` ~L305 checks only Nightly and DlssNr, while Update All includes F5.
4. **NR DLL on update:** updating an F5 install doesn't refresh `nvngx_dlssnr.dll`. `UpdateAsync` ~L1356 checks `isDlssNr` only.
5. **Update check vs update target:** the update check and Update All use the game's current choice (`GetOsVariant`), but Update and Uninstall use what was installed (`record.OsVariant`). They can disagree after the user changes the version without reinstalling.

### Outside RHI
- **Forks.** Every drop-in build has `OptiScaler.dll` at the archive root. Formats are `.7z` (official) or `.zip` (most forks). The main differences are optional companion zips (F5's `Optional-*.zip`) and whether the build needs NVIDIA's `nvngx_dlssnr.dll` beside it (F5 and DLSS NR do; RHI already adds it). Active builds:

  | Build | Repo | Notes |
  |---|---|---|
  | Official stable | optiscaler/OptiScaler | v0.9.4, July, flat layout |
  | Official nightly | optiscaler/OptiScaler-nightly | daily, all prerelease |
  | F5 | janblade/OptiScaler-F5-DLSSNR-Multipass | every 1–4 days; asset renamed at 0.1.37 |
  | DLSS NR | wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass | all prerelease, two asset variants |
  | ShyVortex NR-PreSR-MFG | ShyVortex | |
  | Venom | LorenzoKy | daily `.7z` prereleases |
  | y4my4my4m | y4my4my4m | one "nightly" release with ~60 dated assets |
  | FSR-RR | ZachHembree | |
  | OpenNR | | `Setup.ps1` instead of a `.bat` |

  Several "forks" aren't drop-in (AMD-NR at 535 MB with its own launcher, FrameBridge exe only), and GitHub search returns a lot of spam repos.
- **Hybrid Launcher** (LorenzoKy/Hybrid-Launcher, GPL-3.0, by the Venom author; Forks added 4 Nov). This is the tool the reporter is moving to. It has four sources:
  - **Stable** and **Nightly**.
  - **Forks**: 5 built in, plus "Add a fork" by GitHub URL; only `owner/name` is saved.
  - **Custom build**: imports a zip/7z/rar or a folder and never updates.

  How it handles them:
  - **Releases:** lists `/releases?per_page=30`, prereleases included, sorted by `published_at`, cached 30 minutes.
  - **Assets:** ranks them: 7z/zip only, prefers names containing "optiscaler", demotes optional, forwarder, troubleshooting and source archives.
  - **Integrity:** checks downloads against GitHub's `size` and `digest`.
  - **Updates:** tracked per source by release tag.
  - **Custom imports:** finds the DLL up to 3 folders deep; borrows a missing ini or bat from the latest nightly.
- **OptiScaler Client:** repos are fixed by its developer; "Import custom" does no validation and no updates. **Vortex** handles updates only for Nexus mods.
- **Recognising a build.** In all 7 builds checked, the DLL's `ProductName` is exactly "OptiScaler" and `OriginalFilename` is exactly "OptiScaler.dll". Compare exactly: F5's forwarder `nvngx.dll_dlssnr.dll` also contains the word "OptiScaler". `ProductVersion` (e.g. `10.0.0-dev (d94fb15f) (20261107_105422)`) is good for display but can't tell forks apart, since forks keep upstream's number. Track updates by release tag and publish date.
- **GitHub details:**
  - Don't use `/releases/latest`: it skips prereleases, so for wilsjo2 it returns v0.8.3 instead of v0.8.10.
  - Sort by publish date, not tag name (wilsjo2 published v0.8.91 before v0.8.10).
  - Skip drafts and source zipballs.
  - Actions artifacts aren't needed: none of the forks surveyed rely on them.
  - Without a token the limit is 60 requests an hour, so ETag caching matters.

## Recommended design

### Phase 0: one "OptiScaler build source" model, and the F5 fixes

> **Status (8 Nov 2026):** done in a lighter form. `Services/OsVariants.cs` is the one list of versions (built-ins + custom) that the dropdowns, labels, validation, OptiScaler 10 / NR-DLL checks and INI choice (`OptiScalerService.IniSuffix`) read. The four built-ins keep their own download code (it works and each has quirks); forks use their own generic GitHub source (`OsForkService`).

Replace the per-version copies with one data-driven source definition. The four built-ins become entries, and custom builds become more entries of the same kind.

```
OsBuildSource {
  Id            "Stable" | "Nightly" | "DlssNr" | "F5" | "Custom:<slug>"
  Label         "F5 DLSSNR", "Venom (LorenzoKy)", …
  Kind          GitHubReleases | LocalImport
  Repo          "janblade/OptiScaler-F5-DLSSNR-Multipass"   (GitHub only)
  IncludePrereleases, AssetPattern (optional regex), PinnedTag (optional)
  TagCleanup    none | strip "v" | strip "nightly-"
  NeedsNrDll    bool    (adds nvngx_dlssnr.dll, like DLSS NR / F5 today)
  StagingDir    %LocalAppData%\RHI\optiscaler-custom\<slug>\  (built-ins keep their folders)
}
staged state (source.json in the staging dir): tag, published_at, asset name, size, digest, DLL ProductVersion
```

- **One code path:** one `EnsureStagingAsync(source)`, `CheckForUpdateAsync(source)`, `ClearStaging(source)`, `IsStagingReady(source)`, `HasUpdate(source)` and `StagedVersion(source)`. Asset choice uses Hybrid Launcher's ranking, then the optional pattern. The extractor is chosen by file extension (7-Zip for `.7z`, `ZipFile` for `.zip`).
- **OptiScaler 10 detection:** decided by the staged build's layout (`OptiScaler\` subfolder present), never by name. This fixes the inconsistency listed above.
- **Nightly:** keeps its dated-folder layout and per-game build picker. A later step could generalise the picker into "pin a release" for every source.
- **F5 gaps:** go away as a side effect. They can also be fixed on their own now as small bug fixes, which I'd recommend so they don't wait for this feature.

This is the large step: about 20 files. It changes no behaviour for users except the F5 fixes, so it can ship and be checked before custom builds go on top.

### Phase 1: custom builds (decided 7 Nov 2026)

> **Status (8 Nov 2026):** built, including drag and drop (an archive or dropped link with an `OptiScaler.dll` and no RenoDX addon asks to add it, `DragDropHandler.OfferCustomOptiScalerBuildAsync`). Code: `Services/OsCustomBuildService.cs` (import, store, rename, remove), `SettingsHandler.OsCustomBuilds.cs` (Settings card), custom branches in `OptiScalerService.Install.cs`. Tests: `OsCustomBuildTests`. Differences from the text below: the list lives in the folders themselves (no `OsCustomBuilds` in settings.json), and a build that ships its own `OptiScaler.ini` still gets RHI's GPU-specific templates on install (its own ini is the base on update, as for the built-ins).


**A custom build is a fixed copy the user brings, from anywhere.** RHI installs it like any other OptiScaler and never checks it for updates: making sure it's legit and keeping it current is up to the user. To change version they add the new one (or re-import over the same entry).

- **Ways to add one** ("Custom builds" card in Settings › Components › OptiScaler, modelled on the Custom game folders card; drag and drop anywhere on RHI too):
  - **Download link**: any direct link, such as a GitHub release asset, a Discord attachment or a file host. Downloaded once through the shared HttpClient. No GitHub API, no token, no update checks.
  - **File**: zip, 7z or rar (7-Zip for everything but zip).
  - **Folder**: an already-unpacked build.
  - **Drag and drop**: an archive or folder dropped on RHI asks "Add as a custom OptiScaler build?". Both drop paths (`Grid_Drop` and `HandleWin32Drop`) need it.
- **Name**: the user names it when adding, prefilled from the file name. The list shows name, the DLL's own version text (`ProductVersion`), where it came from, and the date added, with Rename, Re-import and Remove.
- **Stored** in `%LocalAppData%\RHI\optiscaler-custom\<slug>\` with a `source.json` holding name, origin (link or file name), added date and the DLL version text. Listed in settings.json as `OsCustomBuilds`.
- **Checks (light, by decision)**: an `OptiScaler.dll` must exist (searched up to 3 folders deep; that folder becomes the build root), and every archive entry goes through `FileHelper.SafeCombine`. The DLL's version info is shown, not enforced: a build whose DLL doesn't say "OptiScaler" gets a one-line note when added, but can still be added.
- **Choosing it**: custom builds appear in the per-game OptiScaler ⚙ version dropdown and in Settings' Default version, after the built-ins. Stored value `Custom:<slug>` in the existing `OsVariantOverrides`; the validation lists accept any registered Id.
- **Treated as OptiScaler**: install, uninstall, `rhi_install.txt`, plugins folder, MFG plugin hosting, DLSS DLLs, presets, Engine.ini tweaks all work as for the built-ins, driven by the build's layout (an `OptiScaler\` subfolder means an OptiScaler 10 build).
- **INI**: the build's own `OptiScaler.ini` is the template, with RHI's enforced keys on top (ReShade loading, hotkey, ASI plugins). No ini in the build: the Nightly templates for OptiScaler 10 builds, Stable's for flat ones.
- **Neural Rendering DLL**: per-build switch "Needs the DLSS NR DLL", on automatically when the build contains the `nvngx.dll_dlssnr.dll` forwarder. The ⚙ shows its Neural Rendering section when it's on; frame generation and Engine.ini sections for OptiScaler 10 builds.
- **Updates**: none. Custom builds never show "update available" and are skipped by the periodic check, Update All and auto-update. Re-importing an entry offers to redeploy it to the games using it (like the Custom ReShade channel's hash check).
- **Removing a build while games use it**: ask first; those games keep their files and show "Custom build removed": reinstall or pick another version.

**Recommended forks (separate from custom builds)** (built 8 Nov 2026: `Services/OsForkService.cs`, manifest `osForks`, variant `Fork:<id>`; see `docs/manifest-field-reference.md`): a manifest field `osForks` (live data) lists known-good GitHub forks (repo, label, asset pattern, NeedsNrDll). These are maintained by the developer and *do* get update checks, exactly like F5 today, through the Phase 0 source model. Adding one to the manifest makes it appear as a built-in-style version for everyone, without an RHI release.

### Phase 2 (later, if wanted)

> **Done 8 Nov 2026:** per-game Release picker for Stable / DLSS NR / F5 / forks (`OsReleaseService`, pins in `optiscaler-pinned\<version>\<tag>\`, sharing the per-game `OsNightlyBuild` value), and the ⚙'s installed-vs-latest line with Check now / Update (`MainWindow.Events.Components.OptiScaler.Release.cs`). Still open: opt-in companion archives.
- Pin a specific release per game for the GitHub-backed versions (generalising the Nightly build picker).
- Opt-in companion archives (F5's `Optional-*.zip` overlays).

## Effort and order
1. F5 gap fixes: small, can go in the next beta.
2. Phase 0 refactor: large, about 20 files. Best done by a sub-agent with this doc, then tested build by build (install, update, uninstall, Update All for each of the four).
3. Phase 1: medium. The new Settings card, the GitHub source and import flows, and cog and Default version entries. The download side is mostly the Phase 0 code with a new source.

## Decisions (7 Nov 2026)
1. Curated forks in the manifest (`osForks`, updated like F5) **and** user-added custom builds.
2. Fix the F5 gaps now, separately.
3. Custom builds get the ⚙'s advanced sections: frame generation and Engine.ini for OptiScaler 10 builds, Neural Rendering when "Needs the DLSS NR DLL" is on.
4. Custom builds come from anywhere (GitHub, Discord, any link, file, folder, drag and drop). The user is responsible for them being legit and current: RHI only checks there's an `OptiScaler.dll` to install and never updates them.
