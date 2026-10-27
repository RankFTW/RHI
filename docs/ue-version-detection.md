# Unreal Engine Version Detection via PE Scan

How RHI identifies the Unreal Engine version from a game's install folder.

---

## What you need

A Windows PE executable from the game install — RHI tries these in order:

1. `Engine\Binaries\Win64\CrashReportClient.exe`
2. `Engine\Binaries\Win64\CrashReportClient-Win64-Shipping.exe`
3. `Engine\Build\Build.version` (JSON — UE4 games)
4. `*-Win64-Shipping.exe` in the game's `Binaries\Win64\` folder
5. Largest `.exe` in the install directory (fallback)

The key insight: Epic embeds the engine version in the `FileVersion` of their own engine binaries. `CrashReportClient.exe` is the most reliable — it ships with nearly every UE game and its version directly reflects the engine build.

---

## The PE version resource

RHI uses `FileVersionInfo.GetVersionInfo(exePath)` — the same as right-clicking a DLL in Explorer and checking Properties → Details. The fields used:

| Field | Meaning |
|---|---|
| `FileMajorPart` | UE major version (4 or 5) |
| `FileMinorPart` | UE minor version (e.g. 27 for 4.27, 4 for 5.4) |
| `FileBuildPart` | Patch version (e.g. 2 for 4.27.2) — omitted if 0 |

Valid range: major version 4–6. Anything outside that is not a UE binary.

Result format: `"5.4.3"`, `"4.27.2"`, `"5.1"` etc.

---

## Build.version JSON (UE4 fallback)

Some UE4 games don't include `CrashReportClient.exe`. The file `Engine\Build\Build.version` contains:

```json
{
  "MajorVersion": 4,
  "MinorVersion": 27,
  "PatchVersion": 2,
  ...
}
```

RHI reads `MajorVersion`, `MinorVersion`, and `PatchVersion` directly.

---

## Finding the Engine folder

RHI's install path is typically `{Root}\{Project}\Binaries\Win64`. To find the Engine folder it walks upward from the `Binaries` segment, checking for `Engine\Binaries\Win64\` at up to 3 levels above. If not found it also walks up from the raw install path up to 4 levels.

Example layout:
```
MyGame\                          ← gameRoot (Engine folder found here)
  Engine\
    Binaries\
      Win64\
        CrashReportClient.exe    ← version read from here
    Build\
      Build.version
  MyGame\
    Binaries\
      Win64\
        MyGame-Win64-Shipping.exe
```

---

## Quick manual check (PowerShell)

```powershell
$exe = "C:\Path\To\Game\Engine\Binaries\Win64\CrashReportClient.exe"
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
"UE $($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
```

Or from `Build.version`:

```powershell
$json = Get-Content "C:\Path\To\Game\Engine\Build\Build.version" | ConvertFrom-Json
"UE $($json.MajorVersion).$($json.MinorVersion).$($json.PatchVersion)"
```

---

## What RHI does with the version

The detected version string (e.g. `"Unreal Engine 5.4.3"`) is stored as `card.EngineHint`. This drives:

- Whether UE-Extended or a named mod is used for RenoDX
- Whether Engine.ini HDR keys are deployed (UE5 = `Set_Path=0`, UE4 = `Set_Path=1`)
- Whether DOF Fix is shown (UE 5.0–5.6 only)
- Whether the API badge defaults to DX12 (UE5+) or DX11 (UE4)
- Whether the engine badge is clickable (locked when version comes from manifest `engineHintOverrides`)
