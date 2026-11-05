# Nexus Mods API Approval — Response & Action Plan

## Their Demands

> 1. Your manager appears to have credential leakage. The CrashReporter.cs:100-110 logs permanently to %LocalAppData%\RHI\logs containing a lot of information openly such as user IDs which it should not.
> 2. Automated downloads. The AUP expressly objects to API keys being used "without the action being initiated by the user." A user opt-in would be sufficient.
> 3. There appears to be some references to GraphQL in your code. Is that intended?

---

## Our Response to Each Point

### 1. Credential Leakage in Session Logs

**Their concern:** Session logs at `%LocalAppData%\RHI\logs\session_*.txt` contain full file paths (e.g. `C:\Users\Mark\...`, Steam `userdata\{steamId}\...` paths) and Xbox AUMIDs. Logs are written by `CrashReporter.Log()` which calls `AppendSessionLog()` at lines 100–110 of `CrashReporter.cs`.

**Note:** These are local logs stored on the user's own machine and never transmitted anywhere. However Nexus consider logging this data at all to be a privacy concern.

**Our fix (deferred to next update):** Sanitise paths before writing to the session log:
- Replace `C:\Users\{username}\` → `%USERPROFILE%\` 
- Replace full Steam `userdata\{numericId}` paths → `userdata\[userid]\`
- Truncate or mask Xbox AUMIDs — log only the game name, not the full package family string
- Implementation: add a `SanitisePath(string entry)` static helper to `CrashReporter.cs` that runs a regex substitution before `AppendSessionLog` is called

**Files to change:** `g:\RDXC\RenoDXCommander\Services\CrashReporter.cs`

---

### 2. Automated API Calls Without User Action

**Their concern:** `NexusUpdateService.CheckForUpdatesAsync` (which POSTs to `api.nexusmods.com/v2/graphql`) fires automatically:
- On every app startup, via `RunBackgroundScanAndMergeAsync`
- Every 4 hours via the periodic update timer in `MainViewModel.Update.cs`

The AUP requires API usage to be initiated by the user. A user opt-in toggle is sufficient.

**Our fix (deferred to next update):**

Add a **"Background Update Checks" combo box** next to the existing Check for Updates button in Settings. Two states:
- **On** (default): current behaviour — full update checks on startup and every 4 hours, including Nexus GraphQL calls
- **Minimal**: manifest files only on startup/timer (RHI manifest, DLSS manifest, rhi-repo DB). All component update checks (RenoDX addons, ReShade, RE Framework, ReLimiter, DC, OptiScaler, Nexus) only fire when the user explicitly clicks Refresh or Update All

The Nexus GraphQL calls specifically are gated by the new setting. When set to Minimal, they only run on explicit user action.

**Files to change:**
- `g:\RDXC\RenoDXCommander\ViewModels\SettingsViewModel.cs` — add `BackgroundUpdateChecks` property (persisted)
- `g:\RDXC\RenoDXCommander\ViewModels\MainViewModel.Update.cs` — gate `CheckForUpdatesAsync` and Nexus check behind setting
- `g:\RDXC\RenoDXCommander\SettingsHandler.cs` — add combo box to Settings UI next to Check for Updates button

---

### 3. GraphQL Usage

**Their question:** Is the GraphQL usage intentional?

**Our answer:** Yes. RHI uses the Nexus Mods GraphQL v2 API (`api.nexusmods.com/v2/graphql`) for:
- **Update detection**: querying `legacyModsByDomain` to check if a newer mod version is available (compares installed version string against the latest `version` field on the node)
- **Mod summaries**: fetching the `summary` field for the Info button dialog

No API key is used. The GraphQL endpoint is called anonymously. Nexus confirmed in earlier correspondence that anonymous GraphQL usage is permitted.

**Confirm in reply:** State explicitly that GraphQL is intentional and used for update detection and mod summary display. No authentication, no API key.

---

## Draft Reply to Nexus

> Hi,
>
> Thank you for the detailed feedback. Here's our response to each point:
>
> **1. Session logs:** You're correct that our session logs write full file paths including usernames and AUMIDs to the user's local AppData folder. These logs are never transmitted outside the user's machine, but we understand your concern about logging this data at all. We will sanitise paths in the session log before the next release — replacing usernames with %USERPROFILE%, masking Steam user IDs in paths, and truncating Xbox AUMIDs to the game name only.
>
> **2. Automated API calls:** We will add a "Background Update Checks" setting that defaults to On (current behaviour) but can be set to Minimal, which restricts automatic background calls to manifest fetches only. All Nexus GraphQL calls in Minimal mode will only fire when the user explicitly initiates a Refresh or Update All action. This opt-in toggle will be prominently placed next to the existing update check controls.
>
> **3. GraphQL:** Yes, this is intentional. We use the Nexus Mods GraphQL v2 API anonymously (no API key) for two purposes: checking whether a newer version of an installed mod is available, and fetching mod summaries for the in-app info dialog. We are not using any authenticated endpoints.
>
> We will implement both fixes before our next public release and can share the updated code for review at that point.
>
> Kind regards,
> Mark

---

## Implementation Checklist (Next Update)

- [ ] `CrashReporter.cs` — add `SanitisePath()` helper, apply to `Log()` before `AppendSessionLog()`
- [ ] `SettingsViewModel.cs` — add `BackgroundUpdateChecks` string property ("On" / "Minimal"), load/save
- [ ] `MainViewModel.Update.cs` — gate `CheckForUpdatesAsync` Nexus path behind `Settings.BackgroundUpdateChecks == "On"`
- [ ] `SettingsHandler.cs` — add ComboBox next to Check for Updates button, wire to `BackgroundUpdateChecks`
- [ ] Reply to Nexus with the draft above
- [ ] Share updated code with Nexus for review before next public release
