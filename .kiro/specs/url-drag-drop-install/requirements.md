# Requirements Document

## Introduction

This feature enables users to drag an addon URL link (e.g. from Discord or a browser) directly onto the RHI application window to automatically download and install the addon. Currently, RHI supports drag-and-drop of local addon files via Win32 WM_DROPFILES handling. This feature extends that mechanism to also accept URLs pointing to remote `.addon64`/`.addon32` files, downloading them to the local cache before routing through the existing game-matching and install flow.

## Glossary

- **RHI**: The RenoDX Helper/Installer desktop application (WinUI 3, unpackaged).
- **Addon_File**: A ReShade/RenoDX addon binary with `.addon64` or `.addon32` extension.
- **URL_Drop**: A drag-and-drop operation where the dragged data contains a URL string rather than a local file path.
- **Download_Cache**: The local directory at `%LocalAppData%\RHI\downloads\` used to cache downloaded addon files.
- **PE_Binary**: A Windows Portable Executable file identified by the `MZ` magic bytes at offset 0.
- **DragDropHandler**: The existing service class that processes dropped files and routes them to game-matching and install flows.
- **WindowStateManager**: The class that subclasses the Win32 WndProc to intercept WM_DROPFILES and route to DragDropHandler.

## Requirements

### Requirement 1: Detect URL drops in the WndProc message handler

**User Story:** As a user, I want to drag a URL from Discord or a browser onto the RHI window, so that RHI recognizes it as an addon URL and begins processing it.

#### Acceptance Criteria

1. WHEN a drag-and-drop operation delivers data containing a URL string (via CF_UNICODETEXT or CF_TEXT clipboard format from OLE drag-and-drop, or via a `.url` shortcut file from WM_DROPFILES), THE WindowStateManager SHALL detect the URL and route it to the DragDropHandler for URL processing.
2. WHEN a WM_DROPFILES message delivers a file path ending in `.url`, THE WindowStateManager SHALL read the URL from the shortcut file and route it to the DragDropHandler for URL processing.
3. WHEN the dropped data contains both a local file path and a URL, THE WindowStateManager SHALL prefer the local file path and process it using the existing file drag-and-drop flow.

### Requirement 2: Validate dropped URLs

**User Story:** As a user, I want RHI to only accept URLs that point to valid addon files, so that I don't accidentally trigger downloads of unrelated files.

#### Acceptance Criteria

1. WHEN a URL is received from a drop operation, THE DragDropHandler SHALL extract the filename from the URL path component, stripping any query parameters and fragment identifiers.
2. WHEN the extracted filename has a `.addon64` or `.addon32` extension, THE DragDropHandler SHALL accept the URL for download processing.
3. WHEN the extracted filename does not have a `.addon64` or `.addon32` extension, THE DragDropHandler SHALL reject the URL and log a message indicating the unsupported file type.
4. IF the URL cannot be parsed as a valid URI, THEN THE DragDropHandler SHALL reject the drop and log a descriptive error message.

### Requirement 3: Download the addon file from the URL

**User Story:** As a user, I want RHI to download the addon file from the URL to the local cache, so that it can be installed to my game.

#### Acceptance Criteria

1. WHEN a valid addon URL is accepted, THE DragDropHandler SHALL download the file to the Download_Cache directory (`%LocalAppData%\RHI\downloads\`) using the extracted filename.
2. WHILE the download is in progress, THE DragDropHandler SHALL display a progress dialog showing the download status and percentage (when Content-Length is available).
3. IF the download fails due to a network error or non-success HTTP status code, THEN THE DragDropHandler SHALL display an error dialog to the user with a descriptive message.
4. IF a file with the same name already exists in the Download_Cache, THEN THE DragDropHandler SHALL overwrite it with the newly downloaded file.
5. THE DragDropHandler SHALL download to a temporary file (`.tmp` suffix) first, then rename to the final filename upon successful completion, to avoid leaving partial files in the cache.

### Requirement 4: Validate the downloaded file

**User Story:** As a user, I want RHI to verify that the downloaded file is a valid addon binary, so that corrupted downloads or HTML error pages are not installed.

#### Acceptance Criteria

1. WHEN a download completes, THE DragDropHandler SHALL verify the downloaded file is a PE_Binary by checking for the `MZ` magic bytes at the start of the file.
2. IF the downloaded file is not a valid PE_Binary, THEN THE DragDropHandler SHALL delete the cached file and display an error dialog informing the user that the download was not a valid addon file.
3. WHEN the downloaded file passes PE validation, THE DragDropHandler SHALL proceed to the game-matching and install flow.

### Requirement 5: Route downloaded addon through existing install flow

**User Story:** As a user, I want the URL-dropped addon to go through the same game-matching and install process as a locally dropped addon file, so that the experience is consistent.

#### Acceptance Criteria

1. WHEN a downloaded addon file passes validation, THE DragDropHandler SHALL invoke the existing `ProcessDroppedAddon` method with the cached file path.
2. THE DragDropHandler SHALL present the same game-selection picker dialog as the existing local file drag-and-drop flow, including auto-matching the addon filename to a game name.
3. THE DragDropHandler SHALL present the same confirmation dialog showing existing addons and install path before proceeding with installation.

### Requirement 6: Provide user feedback during URL drop processing

**User Story:** As a user, I want to see clear feedback when I drop a URL onto RHI, so that I know the app recognized my action and is working on it.

#### Acceptance Criteria

1. WHEN a URL drop is detected and accepted, THE DragDropHandler SHALL display a progress dialog with the message indicating the addon filename being downloaded.
2. WHILE the download is in progress, THE DragDropHandler SHALL update the progress dialog with download progress (bytes downloaded, percentage when available).
3. WHEN the download completes and validation passes, THE DragDropHandler SHALL dismiss the progress dialog and proceed to the game-selection flow.
4. IF the URL is rejected (invalid extension or unparseable), THEN THE DragDropHandler SHALL display a brief error dialog explaining why the URL was not accepted.

### Requirement 7: Handle URL filename extraction

**User Story:** As a developer, I want URL filename extraction to be robust against various URL formats, so that addon filenames are correctly determined from CDN URLs with query parameters.

#### Acceptance Criteria

1. THE DragDropHandler SHALL extract the filename from the URL path component only, ignoring query string parameters (e.g. `?ex=...&is=...&hm=...`).
2. THE DragDropHandler SHALL extract the filename from the last path segment of the URL (e.g. `renodx-crimsondesert.addon64` from `https://cdn.discordapp.com/attachments/.../renodx-crimsondesert.addon64?ex=...`).
3. THE DragDropHandler SHALL handle URLs with URL-encoded characters in the filename by decoding them before use.
4. FOR ALL valid addon URLs, extracting the filename then reconstructing a path with that filename SHALL produce a valid filesystem path with the correct `.addon64` or `.addon32` extension.
