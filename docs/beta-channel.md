# Beta channel

How RHI betas work and how to release one. Set up November 2026 (v2.9.5 Beta).

## The idea

- **One code repo.** All work happens on `main` in RankFTW/RHI, as before.
- **RankFTW/rhi-beta holds beta releases only:** no code, just GitHub releases with `RHI-Setup.exe` and the notes. It's public, so downloads need no token.
- **Live data stays in RHI `main`.** `manifest.json`, `dlss_manifest.json`, `optiscaler_presets.json`, `motd.md` and `database/` are read from RHI `main` by every build, beta or release.
- Users opt in with **Settings › General › Updates › Get beta updates** (saved as `GetBetaUpdates`; the RDXC-era `BetaOptIn` key is ignored on purpose).

## Versions

A beta keeps the number of the release it leads to, and carries the beta as a label:

| Build | `AssemblyVersion` / `FileVersion` | `RhiBeta` | Shown as | Tag |
|---|---|---|---|---|
| 2.9.5 Beta 1 | 2.9.5.0 | 1 | 2.9.5 beta 1 | `RHI-2.9.5-beta.1` (rhi-beta) |
| 2.9.5 Beta 2 | 2.9.5.0 | 2 | 2.9.5 beta 2 | `RHI-2.9.5-beta.2` (rhi-beta) |
| 2.9.5 | 2.9.5.0 | *(empty)* | 2.9.5 | `RHI-2.9.5` (RHI) |
| 2.9.5.1 hotfix | 2.9.5.1 | *(empty)* | 2.9.5.1 | `RHI-2.9.5.1` (RHI) |

- The 4th number is a **real version**: 2.9.5.1 is a hotfix after 2.9.5, never a beta.
- `RhiBeta` in `RenoDXCommander.csproj` sets `InformationalVersion` (`2.9.5.0-beta.1`), which is the exe's Product version. RHI reads the number from the assembly and the beta from that label (`Services/AppVersion.cs`, `AppVersionInfo`).
- Order: 2.9.4 < 2.9.5 beta 1 < 2.9.5 beta 2 < 2.9.5 < 2.9.5.1 < 2.9.6 beta 1.

Where the version shows: bottom-corner version button, About, session log header, crash reports, Copy Report (`rhiVersion`), the update dialog. The "What's New" dialog shows once per version including the beta (marker `PatchNotes-2.9.5-beta.1.txt`).

## What RHI offers

`UpdateService.CheckForUpdateAsync` → `VersionResolver.Resolve`:

```
candidates = latest release (RankFTW/RHI)  +  latest beta (RankFTW/rhi-beta, only with Get beta updates on)
offer the newest candidate, if it's newer than the installed build
```

- Beta users get each new beta, then the release of that version when it's out (2.9.5 beta 2 → 2.9.5).
- A beta is only offered while it's ahead of the latest release; an old beta of a released version never is.
- Turning the setting off never downgrades: a beta user stays put until a newer release.
- Both repos are read through the API (`/releases/latest`, ETag-cached) **and** the website redirect `github.com/<repo>/releases/latest` (not rate limited); the newer answer wins.
- `/releases/latest` skips pre-releases, so **betas must be published as normal releases** in rhi-beta (the script does this).

Tests: `RenoDXCommander.Tests/AppVersionTests.cs` (parsing, ordering, every offer case above, the assembly label).

## Releasing a beta

1. In `RenoDXCommander/RenoDXCommander.csproj` set `AssemblyVersion` / `FileVersion` to the coming release (`2.9.5.0`) and `<RhiBeta>1</RhiBeta>` (2, 3, … for later betas).
2. Patch notes: `RHI_PatchNotes.md` top section `## v2.9.5 Beta N` (whole beta), and `docs/release-notes/v2.9.5-beta-N.md` with only the changes since the previous beta (used as the release notes; without it the script uses the top section of `RHI_PatchNotes.md`).
3. Run:
   ```
   pwsh tools/release-beta.ps1 -DryRun   # build + installer, shows what it would publish
   pwsh tools/release-beta.ps1           # same, then creates the rhi-beta release
   ```
   It closes RHI, runs `publish.bat`, checks the exe's Product version is the beta, builds the installer with Inno Setup (`RHI Setup.iss`, publish folder `C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI`) into `%TEMP%\rhi-beta-release\<tag>\`, and runs `gh release create` on rhi-beta. It refuses if `RhiBeta` is empty or the tag already exists. `-SkipPublish` reuses the exe already built.
4. Commit and push the version change and notes to RHI `main` as usual.

## Releasing the full version

1. Empty `RhiBeta` (`<RhiBeta></RhiBeta>`), keep `2.9.5.0`.
2. Rename the patch-notes heading to `## v2.9.5`.
3. Publish, build the installer and release in RankFTW/RHI as today (tag `RHI-2.9.5`). Beta users move to it automatically.

## Hotfixes while a beta is in progress

`main` has beta code, so branch from the release tag: `git switch -c hotfix/2.9.5.1 RHI-2.9.5`, fix, set `AssemblyVersion` / `FileVersion` to `2.9.5.1` (`RhiBeta` empty), release in RHI as `RHI-2.9.5.1`, then merge the branch back into `main`. Beta users on 2.9.6 betas aren't offered it (their beta is newer); 2.9.5 users are.

## Same installer

Betas and releases use the same installer (same AppId), so either installs over the other and keeps settings. Inno's `AppVersion` is the number (2.9.5.0) for every beta.
