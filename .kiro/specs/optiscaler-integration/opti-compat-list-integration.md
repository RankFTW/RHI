# OptiScaler Compatibility List Integration — Reference Document

## Goal
Fetch and parse the OptiScaler wiki compatibility list, match entries to detected games, and display compatibility status (working/not working, supported inputs, notes) on the OptiScaler row in the detail panel.

## Data Source
- **Raw markdown URL**: `https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md`
- This returns the raw markdown of the wiki page (no HTML parsing needed — it's a markdown table)
- The wiki is community-maintained and updated multiple times per day
- ~600 working games, ~4 not working

## Table Format (Markdown)
The table has 6 columns with this header:

```
| Game | Windows | Linux | Upscaler <br>Inputs | Notes | Images |
| ---- | :-: | :---: | :----: | ------- | :----: |
```

### Column Details
1. **Game** — Game name, sometimes wrapped in a wiki link like `[Alan Wake 2](Alan-Wake-II)` or just plain text like `687 Days on Mars Demo`
2. **Windows** — `✔️` (working) or `❌` (not working) or empty
3. **Linux** — `✅` (working on Linux) or `⛔` (not working on Linux) or empty
4. **Upscaler Inputs** — Comma-separated list: `DLSS`, `FSR2`, `FSR3`, `FSR3.1`, `FSR3.1/4`, `XeSS`, `FSR4` etc.
5. **Notes** — Free text with markdown formatting (bold, links, code blocks). Key patterns:
   - `Use OptiScaler as winmm.dll` — DLL name requirement
   - `Use -dx12 launch option` or `Create a shortcut with -dx12 launch parameter` — launch parameter needed
   - `Fsr2Pattern=true` — INI setting needed
   - `Dxgi=false` — spoofing setting
   - `Requires REFramework` — dependency
   - `Online game, use at your own risk!` — anti-cheat warning
   - `FG doesn't work` — frame gen limitation
   - `Enable Non-Linear sRGB Input` — FSR4 fix
   - `Use OptiPatcher` — OptiPatcher recommended
6. **Images** — Screenshot links, not useful for RHI

### Example Rows (Raw Markdown)
```
| [Alan Wake 2](Alan-Wake-II) |  ✔️ | ✅ | DLSS |  | [1](...) [2](...) |
| [Atlas Fallen: Reign Of Sand](Atlas-Fallen-Reign-Of-Sand) | ❌ | | FSR2 | OptiScaler cannot hook this game's FSR2 inputs in DX12 or Vulkan. |
| Atomic Heart | ✔️ | ✅ | DLSS, FSR2 | Use `Fsr2Pattern=true` in the .ini for FSR2 inputs | [1](...) |
| [Assassin's Creed Mirage](Assassins-Creed-Mirage) | ✔️ |  | DLSS, XeSS, FSR3.1 | Recommended to disable spoofing, use FSR/XeSS inputs | [1](...) |
```

## Existing Pattern to Follow — WikiService.cs
RHI already parses the RenoDX wiki using `WikiService`:
- **File**: `RenoDXCommander/Services/WikiService.cs`
- **Interface**: `RenoDXCommander/Services/IWikiService.cs`
- **URL**: `https://github.com/clshortfuse/renodx/wiki/Mods`
- **Method**: `FetchAllAsync()` — fetches HTML, parses tables with HtmlAgilityPack
- **Matching**: Uses `GameDetectionService.NormalizeName()` for fuzzy matching
- **DI**: Registered in `App.xaml.cs` service collection

### Key Difference
The RenoDX wiki is HTML (rendered GitHub wiki page) parsed with HtmlAgilityPack. The OptiScaler wiki can be fetched as **raw markdown** from the `raw.githubusercontent.com` URL, which is simpler to parse — just split by `|` pipe characters per line.

## Proposed Implementation

### 1. Model — `OptiScalerCompatEntry`
```csharp
public class OptiScalerCompatEntry
{
    public string GameName { get; set; } = "";
    public bool WorksOnWindows { get; set; }
    public bool WorksOnLinux { get; set; }
    public List<string> UpscalerInputs { get; set; } = new(); // ["DLSS", "FSR3.1", "XeSS"]
    public string? Notes { get; set; }
    public string? WikiPageSlug { get; set; } // e.g. "Alan-Wake-II" for deep link
}
```

### 2. Service — `OptiScalerCompatService`
```csharp
public interface IOptiScalerCompatService
{
    Task FetchAsync();
    OptiScalerCompatEntry? FindEntry(string gameName);
}
```

- Fetch raw markdown from `https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md`
- Parse each table row by splitting on `|`
- Extract game name (strip markdown link syntax `[Name](Slug)`)
- Extract Windows/Linux status by checking for emoji
- Extract upscaler inputs by splitting on `,`
- Store notes as-is (strip markdown formatting for display)
- Match games using `GameDetectionService.NormalizeName()` — same as RenoDX wiki

### 3. Parsing Logic (Markdown Table)
```
For each line after the header separator (| ---- |...):
  1. Split by '|', trim each cell
  2. Cell 0 = empty (before first pipe)
  3. Cell 1 = Game name — regex: \[(.+?)\]\((.+?)\) for linked, or plain text
  4. Cell 2 = Windows status — contains "✔️" → true, "❌" → false
  5. Cell 3 = Linux status — contains "✅" → true, "⛔" → false
  6. Cell 4 = Upscaler inputs — split by "," and trim
  7. Cell 5 = Notes — strip markdown bold/links for display
  8. Cell 6 = Images — ignore
```

### 4. Integration Points
- **Startup**: Fetch in parallel with other wiki/manifest fetches in `MainViewModel.InitializeAsync`
- **BuildCards**: After OptiScaler detection, look up compat entry and store on card
- **Detail Panel**: Show on OptiScaler row — compat badge (✔️/❌), supported inputs, notes tooltip
- **Card ViewModel**: Add `OptiScalerCompat` property to `GameCardViewModel`

### 5. UI Display Options
- **Badge on OptiScaler row**: Green checkmark if compatible, red X if not, nothing if unknown
- **Tooltip on badge**: Shows supported inputs and notes
- **Inputs text**: "DLSS, FSR3.1, XeSS" shown as small text under the OptiScaler status
- **Notes**: Shown in tooltip or expandable section — contains game-specific setup instructions

### 6. Caching
- Cache the parsed data to disk (JSON) with a TTL (e.g. 24 hours)
- On subsequent launches, load from cache and fetch in background to update
- This avoids adding latency to startup

### 7. Actionable Notes (Future Enhancement)
Some notes contain actionable information that RHI could auto-apply:
- `Use OptiScaler as winmm.dll` → Auto-set DLL name override
- `Fsr2Pattern=true` → Auto-set in OptiScaler.ini on install
- `Dxgi=false` under [Spoofing] → Auto-set in OptiScaler.ini on install
- `Use -dx12 launch option` → Show as a tip (can't auto-apply Steam launch options)
- `Requires REFramework` → Show warning if REFramework not installed

These would require structured parsing of the notes field and are a separate feature.

## Game Name Matching Challenges
The OptiScaler wiki uses slightly different game names than Steam/store names:
- Wiki: `DEATH STRANDING DIRECTOR'S CUT` vs Store: `DEATH STRANDING DIRECTOR'S CUT`
- Wiki: `F.I.S.T.: Forged In Shadow Torch` vs Store: varies
- Wiki: `Cyberpunk 2077` — exact match
- Wiki: `DOOM: The Dark Ages` — may need colon handling

The existing `NormalizeName()` function strips punctuation, spaces, and lowercases — this should handle most cases. The same fuzzy matching used for RenoDX wiki will work here.

## Files to Create/Modify
1. **New**: `RenoDXCommander/Models/OptiScalerCompatEntry.cs`
2. **New**: `RenoDXCommander/Services/IOptiScalerCompatService.cs`
3. **New**: `RenoDXCommander/Services/OptiScalerCompatService.cs`
4. **Modify**: `RenoDXCommander/App.xaml.cs` — Register DI
5. **Modify**: `RenoDXCommander/ViewModels/MainViewModel.Init.cs` — Add fetch task, wire into BuildCards
6. **Modify**: `RenoDXCommander/ViewModels/GameCardViewModel.cs` — Add compat property
7. **Modify**: `RenoDXCommander/DetailPanelBuilder.Components.cs` — Show compat on OptiScaler row
