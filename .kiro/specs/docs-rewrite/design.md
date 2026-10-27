# Design Document — docs-rewrite

## Overview

This spec covers a ground-up rewrite of three markdown files:

1. **README.md** — concise project landing page (~200 lines)
2. **docs/DETAILED_GUIDE.md** — exhaustive feature reference
3. **DETAILED_GUIDE.md** (root) — redirect stub pointing to `docs/DETAILED_GUIDE.md`

No code changes are involved. The rewrite uses existing source files (code, FEATURES.txt, current docs) as the source of truth for accuracy.

## Architecture

This is a documentation-only change. There is no software architecture to design. The "architecture" is the relationship between the three files:

```
README.md  ──links to──▶  docs/DETAILED_GUIDE.md
DETAILED_GUIDE.md (root)  ──redirects to──▶  docs/DETAILED_GUIDE.md
```

The README is the entry point. It links to the Detailed Guide for depth. The root redirect ensures anyone who finds the old root-level file gets sent to the canonical location.

## Components and Interfaces

### Component 1: README.md

Target: ~150–200 lines of rendered markdown.

**Section order:**

| # | Section | Content |
|---|---------|---------|
| 1 | H1 Title | "RHI — RenoDX HDR Installer" |
| 2 | One-paragraph summary | WinUI 3 desktop app, manages HDR mod installations for PC games |
| 3 | Screenshot | `![RHI](screenshots/game_view.png)` |
| 4 | Single-player warning | Blockquote warning about anti-cheat |
| 5 | Feature Highlights | Bullet list: auto-detection (8 stores), one-click install, drag-and-drop, per-game overrides, update detection, shader pack management |
| 6 | Quick Start | 4 numbered steps to get HDR mods running |
| 7 | Download & Requirements | Link to GitHub Releases, .NET 8 Desktop Runtime |
| 8 | Troubleshooting | Table of common problems and fixes (8–10 rows) |
| 9 | Third-Party Components | Table: component, author, licence |
| 10 | Acknowledgements | RenoDX team, Crosire credit |
| 11 | Links | Project, community, support URLs |
| 12 | Detailed Guide link | "For comprehensive documentation, see the [Detailed Guide](docs/DETAILED_GUIDE.md)." |

### Component 2: docs/DETAILED_GUIDE.md

Exhaustive reference. No line limit.

**Section order:**

| # | Section | Covers |
|---|---------|--------|
| 1 | H1 Title + intro | "RHI — Detailed Guide", link back to README |
| 2 | Table of Contents | Anchor links to every H2 section |
| 3 | Layout | Detail View, Grid View, toolbar, sidebar, detail panel, status bar |
| 4 | Settings Page | Add Game, Full Refresh, Preferences, Crash/Error Logs, About/Credits |
| 5 | Game Detection | 8 stores with detection methods table, engine detection, bitness detection, manual add, drag-and-drop, addon auto-detection, AddonPath |
| 6 | Graphics API Detection | Detected APIs table, multi-exe scanning, multi-API display rules, automatic DLL naming, manifest API overrides |
| 7 | Components | ReShade, RenoDX, ReLimiter, Luma rows; version display, mod author badges, clickable status links, DLL naming priority chain, non-standard DLL detection |
| 8 | Vulkan ReShade Support | Global layer, per-game INI, lightweight install, dual-API handling, per-game uninstall |
| 9 | Foreign DLL Protection | Binary signature scanning, overwrite confirmation, Update All skip behaviour |
| 10 | UE-Extended & Native HDR | Auto-assignment, toggle behaviour, compatibility warning |
| 11 | ReLimiter | 32/64-bit support, install/update/uninstall, INI config, update detection, status indicators |
| 12 | Shader Packs | Categories (Essential, Recommended, Extra), per-game overrides, deploy destinations, startup deployment, custom shaders |
| 13 | Luma Framework | Luma mode behaviour, install/uninstall, trusted download restrictions, Luma notes |
| 14 | Per-Game Overrides | All override options table with effects |
| 15 | INI Presets | All bundled INI files table, merge strategy, custom preset support |
| 16 | Remote Manifest | All manifest fields table with effects |
| 17 | Update All | Per-component inclusion toggles, foreign DLL skipping |
| 18 | Auto-Update | Stable/beta channels, version resolution logic |
| 19 | Patch Notes | Dialog behaviour |
| 20 | Drag-and-Drop | File types table, UIPI bypass, extension validation |
| 21 | Addon Auto-Detection | Downloads folder watching, named pipe forwarding, filename prefix |
| 22 | AddonPath Support | AddonPath resolution, affected operations |
| 23 | Data Storage | Paths table, session logging, log pruning |
| 24 | Troubleshooting | Expanded table (12–15 rows) |
| 25 | Third-Party Components | Table: component, author, licence (includes HtmlAgilityPack, CommunityToolkit.Mvvm, SharpCompress, 7-Zip) |

### Component 3: DETAILED_GUIDE.md (root redirect)

3–5 lines total:

```markdown
# RHI — Detailed Guide

This document has moved. See [docs/DETAILED_GUIDE.md](docs/DETAILED_GUIDE.md) for the full guide.
```

## Data Models

Not applicable — this is a documentation-only feature. No data models are created or modified.

## Information Sources

The following source files should be referenced for accuracy when writing each document:

| Source File | Used For |
|-------------|----------|
| `RenoDXCommander/FEATURES.txt` | ReLimiter feature details, OSD elements |
| `RenoDXCommander/Models/*.cs` | Data model names, enum values, field names |
| `RenoDXCommander/MainWindow.xaml.cs` | UI layout, toolbar controls, event handlers |
| `RenoDXCommander/MainWindow.Events.cs` | Event handling, drag-and-drop, addon detection |
| `RenoDXCommander/DetailPanelBuilder*.cs` | Detail panel layout, component rows, overrides |
| `RenoDXCommander/CardBuilder*.cs` | Card/grid view layout, badges, status dots |
| `RenoDXCommander/InstallEventHandler.cs` | Install/uninstall/update workflows |
| `RenoDXCommander/DialogService*.cs` | Dialog flows, update dialogs |
| `RenoDXCommander/DragDropHandler*.cs` | Drag-and-drop file handling |
| `RenoDXCommander/Assets/manifest.json` | Shader pack list, categories |
| `docs/DETAILED_GUIDE.md` (current) | Existing documentation as baseline reference |
| `README.md` (current) | Existing README as baseline reference |
| `screenshots/game_view.png` | Screenshot reference path |


## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

Since this is a documentation-only feature, the "system" is the set of markdown files. Properties validate structural and naming invariants across the documents.

### Property 1: No deprecated names

*For any* line in README.md or docs/DETAILED_GUIDE.md, the line should not contain the deprecated strings "RenoDX Commander", "Ultra Limiter", or "ReShade HDR Installer" (the old H1 title), except where explicitly used in historical context.

**Validates: Requirements 2.1, 4.1, 4.2, 4.6**

### Property 2: Valid heading hierarchy

*For any* markdown documentation file (README.md, docs/DETAILED_GUIDE.md), the heading structure should follow a valid hierarchy: exactly one H1, no H3 without a preceding H2, and no heading level skips (e.g. H1 → H3 without H2).

**Validates: Requirements 5.1, 5.2**

### Property 3: Table of Contents anchor integrity

*For any* anchor link in the Detailed Guide's Table of Contents section, there should exist a corresponding heading in the document that matches the anchor target.

**Validates: Requirements 3.1**

### Property 4: Horizontal rules separate major sections

*For any* pair of consecutive H2 headings in docs/DETAILED_GUIDE.md, there should be a horizontal rule (`---`) between them.

**Validates: Requirements 5.5**

## Error Handling

Not applicable — this is a documentation-only feature. There are no runtime errors to handle.

The only "error" scenario is inaccurate content, which is addressed by:
- Using source code files as the source of truth (see Information Sources table)
- Requirements 2.5, 4.3–4.5, 4.7 mandate accuracy checks during writing
- Review by the project maintainer before merge

## Testing Strategy

### Property-Based Tests

Since this feature produces static markdown files (not generated output), traditional property-based testing with randomized inputs does not apply. However, the four correctness properties above can be validated as deterministic file-content checks:

- **Property 1** (no deprecated names): Scan all lines of both files for forbidden strings.
- **Property 2** (heading hierarchy): Parse headings from both files and validate hierarchy rules.
- **Property 3** (ToC anchor integrity): Extract ToC links and match against actual headings.
- **Property 4** (horizontal rules): Parse H2 positions and check for `---` between each pair.

These can be implemented as unit tests that read the markdown files and assert structural invariants. A property-based testing library is not needed since the inputs are fixed files, not randomized — but the properties themselves are universally quantified over all lines/headings in the documents.

### Unit Tests (Example-Based)

Example-based tests should verify specific content requirements:

- README contains all 8 store names (Req 2.2)
- README contains all 5 component names (Req 2.3)
- README contains screenshot reference at `screenshots/game_view.png` (Req 2.4)
- README is under 200 lines (Req 1.11)
- README contains link to `docs/DETAILED_GUIDE.md` (Req 1.10)
- Detailed Guide contains link back to README (Req 5.7)
- Root DETAILED_GUIDE.md contains redirect link and is under 10 lines (Req 6.1, 6.2)
- Detailed Guide contains required section headings for all 25 content requirements (Req 3.2–3.25)
- README contains required section headings (Req 1.1–1.9)
- Tables exist in troubleshooting and third-party sections of both files (Req 5.3, 5.4)

### Recommended Approach

Given that these are static files, the most practical validation is a **manual review checklist** derived from the acceptance criteria, supplemented by a simple script or CI check for the structural properties (deprecated names, heading hierarchy, ToC integrity). Full automated test suites would be overkill for a documentation rewrite.
