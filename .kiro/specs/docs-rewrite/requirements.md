# Requirements Document

## Introduction

Complete ground-up rewrite of the RHI (RenoDX HDR Installer) project documentation. RHI is a WinUI 3 desktop application that manages HDR mod installations for PC games. The project has two primary documentation files: a concise README.md at the repository root and a comprehensive DETAILED_GUIDE.md in the docs/ directory. Both files need to be rewritten from scratch to be accurate, well-structured, and aligned with the current state of the application. This is a documentation-only change — no code modifications are involved.

## Glossary

- **RHI**: RenoDX HDR Installer — the WinUI 3 desktop application that manages HDR mod installations for PC games. Formerly known as RenoDX Commander.
- **README**: The root-level `README.md` file serving as the project's landing page and quick-reference document.
- **Detailed_Guide**: The `docs/DETAILED_GUIDE.md` file providing exhaustive documentation of every RHI feature and workflow.
- **Author**: The documentation writer performing the rewrite.
- **Reader**: Any person reading the documentation, including end users, contributors, and newcomers.
- **Component**: An installable mod or tool managed by RHI (ReShade, RenoDX, ReLimiter, RE Framework, Luma Framework).
- **Remote_Manifest**: A server-hosted JSON file providing game-specific overrides fetched by RHI on launch.
- **Shader_Pack**: A collection of ReShade shader and texture files managed and deployed by RHI.
- **Per_Game_Override**: A user-configurable setting that customises RHI behaviour for a specific game.

## Requirements

### Requirement 1: README Conciseness and Selling Points

**User Story:** As a Reader, I want the README to be short and to the point with the main selling points, so that I can quickly understand what RHI does and why I should use it.

#### Acceptance Criteria

1. THE README SHALL present a one-paragraph project summary describing RHI as a WinUI 3 desktop app for managing HDR mod installations on PC games.
2. THE README SHALL list the main selling points of RHI as a concise feature highlights section covering auto-detection, one-click install, drag-and-drop, per-game overrides, update detection, and shader pack management.
3. THE README SHALL contain a Quick Start section with numbered steps to get a game running with HDR mods.
4. THE README SHALL contain download and system requirements information including a link to the GitHub Releases page and the .NET 8 Desktop Runtime dependency.
5. THE README SHALL contain a single-player-only warning indicating that ReShade with full addon support may be flagged by anti-cheat in online games.
6. THE README SHALL contain a Troubleshooting section with common problems and fixes presented in a table format.
7. THE README SHALL contain a Third-Party Components section listing all bundled or downloaded components with author and licence information.
8. THE README SHALL contain an Acknowledgements section crediting the RenoDX team and Crosire.
9. THE README SHALL contain a Links section with relevant project, community, and support URLs.
10. THE README SHALL link to the Detailed_Guide for comprehensive documentation.
11. THE README SHALL NOT exceed approximately 200 lines of rendered markdown content.

### Requirement 2: README Accuracy

**User Story:** As a Reader, I want the README to contain only correct and current information, so that I am not misled by outdated or inaccurate content.

#### Acceptance Criteria

1. THE README SHALL refer to the application as "RHI" or "RenoDX HDR Installer" consistently throughout the document.
2. THE README SHALL list all eight supported game stores: Steam, GOG, Epic Games, EA App, Ubisoft Connect, Xbox/Game Pass, Battle.net, and Rockstar.
3. THE README SHALL list all five managed components: ReShade, RenoDX, ReLimiter, RE Framework, and Luma Framework.
4. THE README SHALL reference the screenshot file at `screenshots/game_view.png` for the application screenshot.
5. WHEN referencing features, THE README SHALL only describe capabilities that exist in the current version of RHI as documented in the existing source files.

### Requirement 3: Detailed Guide Completeness

**User Story:** As a Reader, I want the Detailed Guide to document every feature of RHI and how it is used, so that I have a single authoritative reference for all functionality.

#### Acceptance Criteria

1. THE Detailed_Guide SHALL contain a Table of Contents with anchor links to every major section.
2. THE Detailed_Guide SHALL contain a section documenting the application layout including Detail View, Grid View, toolbar controls, game list sidebar, detail panel, and status bar.
3. THE Detailed_Guide SHALL contain a section documenting the Settings page including Add Game, Full Refresh, Preferences, Crash and Error Logs, and About/Credits.
4. THE Detailed_Guide SHALL contain a section documenting game detection for all eight supported stores with detection methods for each store.
5. THE Detailed_Guide SHALL contain a section documenting engine detection for Unreal Engine, Unity, and custom engine names from the Remote_Manifest.
6. THE Detailed_Guide SHALL contain a section documenting 32-bit and 64-bit detection via PE header analysis and manifest overrides.
7. THE Detailed_Guide SHALL contain a section documenting graphics API detection including all detected APIs (DX8, DX9, DX10, DX11, DX12, Vulkan, OpenGL), multi-exe scanning, multi-API display rules, and automatic ReShade DLL naming.
8. THE Detailed_Guide SHALL contain a section documenting all managed components (ReShade, RenoDX, ReLimiter, RE Framework, Luma Framework) with install, update, and uninstall workflows.
9. THE Detailed_Guide SHALL contain a section documenting Vulkan ReShade support including global implicit layer, per-game INI, lightweight install, dual-API handling, and per-game uninstall.
10. THE Detailed_Guide SHALL contain a section documenting foreign DLL protection including binary signature scanning and overwrite confirmation.
11. THE Detailed_Guide SHALL contain a section documenting UE-Extended and Native HDR automatic assignment and toggle behaviour.
12. THE Detailed_Guide SHALL contain a section documenting ReLimiter including 32-bit and 64-bit support, INI configuration, update detection, and status indicators.
13. THE Detailed_Guide SHALL contain a section documenting shader packs including categories (Essential, Recommended, Extra), per-game shader overrides, deploy destinations, and startup deployment.
14. THE Detailed_Guide SHALL contain a section documenting Luma Framework including Luma mode behaviour, install and uninstall workflows, and trusted download restrictions.
15. THE Detailed_Guide SHALL contain a section documenting all Per_Game_Overrides including game renaming, wiki mod name mapping, DLL naming override, global update inclusion toggles, shader mode, rendering path, wiki exclusion, and reset.
16. THE Detailed_Guide SHALL contain a section documenting INI presets including all bundled INI files, merge strategy, and custom preset support.
17. THE Detailed_Guide SHALL contain a section documenting the Remote_Manifest including all manifest fields and their effects.
18. THE Detailed_Guide SHALL contain a section documenting Update All behaviour including per-component inclusion toggles and foreign DLL skipping.
19. THE Detailed_Guide SHALL contain a section documenting auto-update including stable and beta channels and version resolution logic.
20. THE Detailed_Guide SHALL contain a section documenting drag-and-drop support for game executables, addon files, archives, and URLs.
21. THE Detailed_Guide SHALL contain a section documenting addon auto-detection from the Downloads folder.
22. THE Detailed_Guide SHALL contain a section documenting AddonPath support for addon deployment.
23. THE Detailed_Guide SHALL contain a section documenting data storage paths and session logging.
24. THE Detailed_Guide SHALL contain a Troubleshooting section covering all common problems and fixes.
25. THE Detailed_Guide SHALL contain a Third-Party Components section listing all bundled or downloaded components with author and licence information.

### Requirement 4: Detailed Guide Accuracy

**User Story:** As a Reader, I want the Detailed Guide to contain only correct and current information, so that I can trust it as a reliable reference.

#### Acceptance Criteria

1. THE Detailed_Guide SHALL refer to the application as "RHI" consistently throughout the document.
2. THE Detailed_Guide SHALL refer to the frame pacing addon as "ReLimiter" consistently throughout the document.
3. WHEN documenting detection methods, THE Detailed_Guide SHALL accurately describe the technical approach used for each game store as documented in the existing source files.
4. WHEN documenting component workflows, THE Detailed_Guide SHALL accurately describe the install, update, and uninstall steps for each component as implemented in the application.
5. WHEN documenting per-game overrides, THE Detailed_Guide SHALL accurately list all available override options and their effects as implemented in the application.
6. THE Detailed_Guide SHALL NOT contain references to deprecated names such as "RenoDX Commander" or "Ultra Limiter" except in historical context where explicitly needed.
7. IF information from the existing documentation conflicts with the current application behaviour, THEN THE Author SHALL use the current application behaviour as the source of truth.

### Requirement 5: Structural and Formatting Standards

**User Story:** As a Reader, I want both documentation files to follow consistent formatting and structural conventions, so that the documentation is easy to navigate and read.

#### Acceptance Criteria

1. THE README SHALL use GitHub-flavoured Markdown with proper heading hierarchy (h1 for title, h2 for sections, h3 for subsections).
2. THE Detailed_Guide SHALL use GitHub-flavoured Markdown with proper heading hierarchy (h1 for title, h2 for sections, h3 for subsections).
3. THE README SHALL use tables for structured data such as troubleshooting entries and third-party components.
4. THE Detailed_Guide SHALL use tables for structured data such as store detection methods, component controls, override options, and troubleshooting entries.
5. THE Detailed_Guide SHALL use horizontal rules to visually separate major sections.
6. THE README SHALL include the application screenshot near the top of the document.
7. THE Detailed_Guide SHALL link back to the README for a quick overview.

### Requirement 6: Root DETAILED_GUIDE.md Redirect

**User Story:** As a Reader, I want the root DETAILED_GUIDE.md to redirect to the canonical docs/DETAILED_GUIDE.md, so that I am always directed to the correct file regardless of which one I find first.

#### Acceptance Criteria

1. THE root `DETAILED_GUIDE.md` SHALL contain a brief heading and a redirect link pointing to `docs/DETAILED_GUIDE.md`.
2. THE root `DETAILED_GUIDE.md` SHALL NOT duplicate the content of the Detailed_Guide.

### Requirement 7: No Code Changes

**User Story:** As a developer, I want the documentation rewrite to be a documentation-only change, so that no application behaviour is affected.

#### Acceptance Criteria

1. THE Author SHALL only modify `.md` files (README.md, docs/DETAILED_GUIDE.md, DETAILED_GUIDE.md).
2. THE Author SHALL NOT modify any `.cs`, `.xaml`, `.csproj`, `.json`, `.txt`, `.ini`, or other non-documentation files.
