# Plan: one-click updates with Velopack

**Status:** future idea, not started (written 5 Nov 2026). Listed in `docs/ROADMAP.md` › Feature Ideas.

## Goal

Replace the Inno Setup installer update with the kind SCSKiller has: RHI downloads the update in the background, the user clicks **Restart to update**, and a few seconds later the new version is running. No installer window, no UAC prompt.

## How SCSKiller does it (github.com/BlueHeisenberg/SCSKiller)

It uses [Velopack](https://velopack.io), an open-source update framework for .NET apps (`src/SCSKiller.Core/App/Updater.cs`):

- Installed per user in `%LocalAppData%\SCSKiller\`: a `current\` folder with the app, `packages\` for downloads, and a small `Update.exe`. Nothing in Program Files, so no admin rights are needed.
- Each GitHub release carries a feed file (`releases.stable.json`) and a package (`SCSKiller.App-1.4.5-stable-full.nupkg`, a zip of the app folder), plus `SCSKiller-Setup.exe` for first installs and a portable zip.
- The app checks the feed at start and every hour, and downloads a newer package in the background.
- "Restart to update" calls `ApplyUpdatesAndRestart`: the app exits, `Update.exe` swaps `current\` for the new files and restarts it. A downloaded update is also applied when the app quits.
- Extras it added: a signed feed (`.sig`, checked before trusting the feed), package checksum checks, and full packages only (deltas are turned off).

## How RHI updates today

- Inno Setup (`RHI Setup.iss`) installs to `C:\Program Files\RHI\` (admin, UAC).
- `UpdateService` checks the GitHub API (`/releases/latest`, plus the beta tag when opted in), downloads `RHI-Setup.exe` and runs it (`LaunchInstallerAndExit`). The installer closes RHI through `%LocalAppData%\RHI\rhi_shutdown_requested`, installs, and relaunches RHI.
- The Inno uninstaller deletes `%LocalAppData%\RHI` (`[UninstallDelete]`), which is RHI's data folder.
- Publish is framework-dependent .NET 8 (`--self-contained false`, so the .NET 8 Desktop Runtime must be installed) with `WindowsAppSDKSelfContained=true`. `Program.cs` already wraps the XAML-generated Main (`DISABLE_XAML_GENERATED_MAIN`).

## Design

### 1. Install location

- Velopack installs to `%LocalAppData%\<packId>\`. **The pack id must not be `RHI`**: `%LocalAppData%\RHI\` is RHI's data folder (settings, logs, artwork, staging), and Velopack's uninstall deletes its whole root folder.
- Proposed pack id `RHI.App`, so the exe becomes `%LocalAppData%\RHI.App\current\RHI.exe`. The data stays in `%LocalAppData%\RHI\`.
- The path stays the same across updates (`current\` is swapped in place), so shortcuts, the jump list and scheduled tasks keep working.
- Everything in `current\` is replaced on each update. Audit anything RHI writes beside its exe (ini templates, caches) and move it to the data folder. `Path.GetDirectoryName(Environment.ProcessPath)` keeps working for reading the shipped files.

### 2. Admin Mode (decide before starting)

Today the "RHI Admin Mode" scheduled task starts `C:\Program Files\RHI\RHI.exe` elevated with no UAC prompt. That's safe because only an admin can change that file. After the move, `RHI.exe` sits in a folder the user can write to. Any program running as the user could replace it and get admin rights silently through the task. That would be a security regression.

Options:

- **(a) Small admin-owned launcher (recommended).** Turning on Admin Mode installs, with one UAC prompt, a tiny launcher in Program Files. The task runs the launcher, which starts RHI elevated only after checking that `RHI.exe` is genuine. "Genuine" means:
  - the launcher checks RHI's Authenticode signature (needs code signing, see 7), or
  - the launcher holds a public key and checks a signed hash list published with each release.
- (b) Keep it as it is and warn in the Admin Mode tooltip. Not recommended.
- (c) Drop UAC-free elevation and use a normal "Run as administrator" prompt for Admin Mode.

### 3. Update channels and versions

- Stable and beta become Velopack channels (`vpk pack --channel beta` → `releases.beta.json`). The existing beta opt-in setting picks the channel (`UpdateOptions.ExplicitChannel`).
- Velopack needs SemVer 2 versions: `2.9.0-beta.4` for "v2.9.0 Beta 4", `2.9.0` for the release. The csproj version stays as it is. Only the `--packVersion` passed to `vpk` needs the mapping, plus the version shown in the UI.

### 4. Where the update comes from

- Option 1: `GithubSource(repoUrl, token, prerelease: betaOptIn)`. One GitHub API call per check, which is fine for checks at start and every 4 h (the existing timer). The token should go through `GitHubTokenHandler`'s rules (api.github.com only), so check how Velopack's downloader sends it.
- Option 2: `SimpleWebSource` on `https://github.com/RankFTW/RHI/releases/latest/download/`. No API calls at all, but beta builds need their own fixed location.
- Velopack checks each package against the SHA in the feed. A signed feed like SCSKiller's is optional and can come later.

### 5. In the app

- `Program.Main`: call `VelopackApp.Build().Run()` first, before WinUI starts. Its hooks handle first run and uninstall.
- Check at start and on the existing 4-hourly timer. Download in the background, reported through `BeginActivity("Downloading update…")`.
- When an update is ready, the current update button becomes **Restart to update**.
  - It stops background work first (the same path as closing the window, so pending settings saves are flushed) and closes RHI even with close-to-tray on.
  - Then it calls `ApplyUpdatesAndRestart`.
- Optional setting: install a downloaded update when RHI quits (`WaitExitThenApplyUpdates`).
- Don't apply an update while an install or download is running in RHI.
- Uninstall hook:
  - remove the Admin Mode task, the start-with-Windows entry and the jump list
  - ask whether to delete `%LocalAppData%\RHI` (today it's always deleted)

### 6. Prerequisites

- The Velopack setup can install the .NET 8 Desktop Runtime (`vpk pack --framework net8-x64-desktop`). The alternative is publishing self-contained: a bigger first download, but delta updates stay small.
- Find out why `Program.cs` can still report "Windows App Runtime 2.5 is missing" with `WindowsAppSDKSelfContained=true` (a VC++ runtime, or a bootstrapper dependency?) and cover it in the setup.
- .NET 8 support ends on 10 Nov 2026. Doing this together with the .NET 10 move (on hold) would test the new install once instead of twice.

### 7. Release steps

- `publish.bat` stays as it is: it already produces the app folder Velopack packs.
- Add a release script that:
  - runs `vpk pack --packId RHI.App --packVersion <semver> --packDir <publish folder> --mainExe RHI.exe --icon RenoDXCommander\icon.ico [--channel beta]`. That makes `RHI.App-Setup.exe`, a portable zip, full and delta packages, and `releases.<channel>.json`.
  - uploads everything to the GitHub release (`vpk upload github` or `gh release upload`)
- Keep delta packages on, so most updates are a few MB.
- Code signing (optional, also helps Admin Mode option (a) and SmartScreen): SignPath offers free signing for open-source projects, which is what SCSKiller uses (its `CODE_SIGNING_POLICY.md`).

### 8. Moving existing users over (one time)

Existing installs are Inno installs in Program Files.

- **Keep the old update path working.** For a few releases, also upload the Velopack setup as `RHI-Setup.exe`. Old versions download and run that name, so they install the Velopack copy even if users skip versions.
- **First run of the Velopack copy:**
  - Re-create the Admin Mode task (one UAC prompt), the start-with-Windows entry and the shortcuts, pointing at the new path.
  - Then remove the old Program Files copy.
- **Pitfall:** the old Inno uninstaller deletes `%LocalAppData%\RHI`, which would wipe users' data. Don't run it as it is.
  - Either ship one last Inno release without `[UninstallDelete]` first,
  - or remove the old folder and its uninstall registry entry directly (elevated).
- Leave both copies working until the new one has started successfully once.

## Testing checklist

- Fresh install with nothing installed (no .NET, no runtime), on Windows 10 and 11.
- Update stable → stable, stable → beta, beta → stable. Update from an old Inno install that skipped several versions.
- Admin Mode on: the update applies, RHI comes back elevated, and the task still points at the right file.
- Close-to-tray on, start with Windows on, desktop shortcut, jump list.
- An update while a game is running, and while an install is running.
- Uninstall: the task and start-with-Windows entry are removed, and the data folder is kept or deleted as chosen.
- No internet, GitHub rate limit, an interrupted download (the next check resumes or downloads again).

## Size and open questions

**Size:** medium.
- About 300–500 lines in RHI: updater, Program hooks, migration, uninstall hook.
- A release script.
- The Admin Mode launcher, if option (a).
- One migration release.

**Open questions:**
1. Admin Mode: option (a), (b) or (c)? With (a), code signing or a signed hash list?
2. Self-contained publish, or let the setup install .NET?
3. Update source: GitHub API or the `/latest/download/` URL?
4. Apply updates on quit by default, or only on "Restart to update"?
5. On uninstall: keep or delete the data folder (today: delete)?
