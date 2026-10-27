# Requirements Document

## Introduction

The app currently blocks the UI for 2–5 seconds on launch while game detection, wiki fetching, manifest fetching, and card building complete. This feature makes the UI appear instantly by loading the cached game library from the previous session, building cards from it, and displaying them immediately. The full detection scan then runs in the background and silently merges fresh results into the already-visible card list.

## Glossary

- **App**: The RenoDX Commander WinUI 3 desktop application.
- **Cached_Library**: The `SavedGameLibrary` JSON file persisted to disk by `GameLibraryService`, containing game entries, addon scan cache, hidden/favourite state, engine caches, and last-selected game.
- **Card**: A `GameCardViewModel` instance representing a single game in the UI, built by `BuildCards()`.
- **Background_Scan**: The full initialization pipeline — platform detection, wiki fetch, manifest fetch, shader pack checks — that currently runs synchronously before the UI appears.
- **Skeleton_Screen**: The placeholder loading UI shown while `IsLoading` is true and `HasInitialized` is false.
- **Merge**: The process of reconciling freshly detected games with the currently displayed cached cards — adding new games, removing stale ones, and updating changed properties.
- **Selection_State**: The currently highlighted/selected `GameCardViewModel` in the game list.
- **Full_Refresh**: A user-initiated rescan (`FullRefreshAsync`) that clears all caches and performs a blocking rescan.

## Requirements

### Requirement 1: Instant Cache Load on Launch

**User Story:** As a user, I want the game list to appear instantly when I launch the app, so that I don't have to wait for detection and network fetches to complete.

#### Acceptance Criteria

1. WHEN the App launches and a Cached_Library exists on disk, THE App SHALL load the Cached_Library and build Cards from it before starting the Background_Scan.
2. WHEN the App launches and a Cached_Library exists on disk, THE App SHALL display the cached Cards within 500 ms of launch.
3. WHEN the App launches and a Cached_Library exists on disk, THE App SHALL suppress the Skeleton_Screen and show the game list directly.
4. WHEN the App launches and no Cached_Library exists on disk, THE App SHALL fall back to the current behaviour: show the Skeleton_Screen and run the full initialization pipeline synchronously.

### Requirement 2: Cached Card Fidelity

**User Story:** As a user, I want the cached cards to look and behave the same as freshly built cards, so that the instant UI feels complete rather than degraded.

#### Acceptance Criteria

1. THE Cached_Library SHALL preserve game name, install path, platform source, installed component statuses, update statuses, user overrides, favourites, and hidden state.
2. WHEN Cards are built from the Cached_Library, THE App SHALL restore addon scan cache, engine type cache, resolved path cache, addon file cache, and bitness cache from the Cached_Library so that card building does not require filesystem traversal.
3. WHEN Cards are built from the Cached_Library, THE App SHALL restore the last-selected game as the active Selection_State.

### Requirement 3: Background Scan and Silent Merge

**User Story:** As a user, I want the app to detect new or removed games in the background and update the list without a visible reload, so that the UI stays responsive and current.

#### Acceptance Criteria

1. WHEN cached Cards are displayed, THE App SHALL start the Background_Scan (platform detection, wiki fetch, manifest fetch, shader pack checks) concurrently.
2. WHEN the Background_Scan completes, THE App SHALL Merge the fresh results into the existing card list: adding Cards for newly detected games, removing Cards for games no longer detected, and updating properties on existing Cards.
3. WHEN the Merge adds or removes Cards, THE App SHALL update the displayed list without clearing and rebuilding the entire collection.
4. WHILE the Merge is in progress, THE App SHALL preserve the current Selection_State — the selected game card SHALL remain selected and visible after the Merge completes.
5. IF the currently selected game is removed during the Merge, THEN THE App SHALL select the first game in the updated list.

### Requirement 4: Full Refresh Bypass

**User Story:** As a user, I want the Full Refresh button to still perform a complete blocking rescan, so that I can force a clean state when needed.

#### Acceptance Criteria

1. WHEN the user triggers a Full_Refresh, THE App SHALL clear all caches and perform a blocking rescan using the current behaviour, ignoring the Cached_Library.
2. WHEN the user triggers a Full_Refresh, THE App SHALL show the Skeleton_Screen during the rescan.

### Requirement 5: Status Bar Feedback

**User Story:** As a user, I want to know when the background scan is running and when it finishes, so that I understand the data may be updating.

#### Acceptance Criteria

1. WHILE the Background_Scan is running after a cache-based launch, THE App SHALL display a status message indicating that a background scan is in progress (e.g. "Library loaded (N games) — scanning for changes...").
2. WHEN the Background_Scan completes, THE App SHALL update the status message to reflect the final game count and installed mod count.
