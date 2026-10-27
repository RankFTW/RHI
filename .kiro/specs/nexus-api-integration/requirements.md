# Requirements Document

## Introduction

This feature integrates the Nexus Mods API into RHI (ReShade HDR Installer) to provide automatic update detection for installed RenoDX mods and download capabilities for both premium and free Nexus Mods users. The integration uses the V2 GraphQL API for metadata queries and the V1 REST API for download link generation, with authentication initially via personal API key (testing phase) and later via OAuth 2.0 + PKCE (public release).

## Glossary

- **RHI**: ReShade HDR Installer — the WinUI 3 desktop application this feature is built into
- **Nexus_API_Client**: The HTTP client service responsible for communicating with Nexus Mods V1 REST and V2 GraphQL APIs
- **Auth_Service**: The service managing Nexus Mods authentication tokens (API key for testing, OAuth 2.0 + PKCE for production)
- **Update_Checker**: The service that queries Nexus Mods for the latest file versions and compares them against locally installed mod versions
- **Download_Service**: The service that handles downloading mod files from Nexus Mods CDN (premium) or via nxm:// protocol (free users)
- **Protocol_Handler**: The Windows registry-based handler that intercepts nxm:// URIs and routes them to RHI for download processing
- **Token_Store**: The encrypted file-based storage in `%LocalAppData%\RHI\` for persisting authentication tokens
- **Game_Domain**: The Nexus Mods URL slug identifying a game (e.g., `cyberpunk2077`, `eldenring`)
- **Mod_ID**: The numeric identifier for a mod on Nexus Mods, extractable from URLs like `nexusmods.com/{game_domain}/mods/{mod_id}`
- **File_ID**: The numeric identifier for a specific file upload within a Nexus Mods mod page
- **NXM_URI**: A protocol URI in the format `nxm://{game_domain}/mods/{mod_id}/files/{file_id}?key={key}&expires={expires}`
- **Premium_User**: A Nexus Mods user with an active premium subscription, eligible for direct API-based CDN downloads
- **Free_User**: A Nexus Mods user without premium, who must use the nxm:// protocol handler flow to download files

## Requirements

### Requirement 1: Nexus Mods Authentication

**User Story:** As a user, I want to link my Nexus Mods account to RHI, so that the app can check for mod updates and facilitate downloads on my behalf.

#### Acceptance Criteria

1. WHEN the user provides a personal API key in Settings, THE Auth_Service SHALL validate the key by calling `GET /v1/users/validate.json` and store it in the Token_Store upon successful validation
2. WHEN the Auth_Service validates a key successfully, THE Auth_Service SHALL extract and persist the user's premium status from the validation response
3. IF the API key validation fails with a 401 response, THEN THE Auth_Service SHALL display an error message indicating the key is invalid and SHALL NOT store the key
4. WHEN the user clicks "Unlink Account" in Settings, THE Auth_Service SHALL delete all stored tokens from the Token_Store and clear the cached user profile
5. THE Auth_Service SHALL include `Application-Name: RHI` and `Application-Version: {current_version}` headers on every request to the Nexus Mods API
6. WHEN a stored API key exists at application startup, THE Auth_Service SHALL re-validate the key in the background and update the premium status

### Requirement 2: Game Domain and Mod ID Resolution

**User Story:** As a user, I want RHI to automatically identify which Nexus Mods page corresponds to each of my installed games, so that update checks work without manual configuration.

#### Acceptance Criteria

1. WHEN a GameMod has a non-null NexusUrl property, THE Update_Checker SHALL parse the game domain and mod ID from the URL pattern `https://www.nexusmods.com/{game_domain}/mods/{mod_id}`
2. WHEN the manifest contains a nexusUrlOverrides entry for a game, THE Update_Checker SHALL parse the game domain from the override URL pattern `https://www.nexusmods.com/games/{game_domain}`
3. IF a NexusUrl cannot be parsed into a valid game domain and mod ID, THEN THE Update_Checker SHALL skip that game and log a warning
4. THE Update_Checker SHALL support both URL formats: `/games/{domain}` (catalogue links) and `/{domain}/mods/{id}` (direct mod links)

### Requirement 3: Mod Update Detection

**User Story:** As a user, I want to see at a glance which of my installed mods have newer versions available on Nexus Mods, so that I can keep my mods up to date.

#### Acceptance Criteria

1. WHEN a game has a resolved Nexus mod ID and the user has a valid API key, THE Update_Checker SHALL query the V2 GraphQL API for the latest file metadata for that mod
2. WHEN the latest Nexus file version differs from the locally installed version, THE Update_Checker SHALL set an update-available flag on the corresponding GameCardViewModel
3. WHILE the update check is in progress, THE Update_Checker SHALL NOT block the UI thread
4. THE Update_Checker SHALL cache update check results with a configurable TTL (default 1 hour) to avoid redundant API calls within the same session
5. WHEN the user manually triggers a refresh, THE Update_Checker SHALL bypass the cache and re-query the API for all resolvable mods
6. IF the Nexus API returns a rate-limit error (HTTP 429), THEN THE Update_Checker SHALL stop further requests and log the rate-limit condition
7. THE GameCardViewModel SHALL expose an `IsNexusUpdateAvailable` property that drives a visual update badge on the game card

### Requirement 4: Update Badge UI

**User Story:** As a user, I want a clear visual indicator on game cards when a Nexus Mods update is available, so that I can quickly identify which mods need updating.

#### Acceptance Criteria

1. WHEN `IsNexusUpdateAvailable` is true on a GameCardViewModel, THE game card SHALL display a visible update badge
2. WHEN the user clicks the update badge or a dedicated "Update from Nexus" button, THE Download_Service SHALL initiate the appropriate download flow based on the user's premium status
3. WHILE no API key is linked, THE game card SHALL NOT display Nexus update badges

### Requirement 5: Premium User Download

**User Story:** As a premium Nexus Mods user, I want RHI to download mod updates directly without opening a browser, so that the update process is seamless.

#### Acceptance Criteria

1. WHEN a premium user initiates a download, THE Download_Service SHALL call `GET /v1/games/{game_domain}/mods/{mod_id}/files.json` to identify the latest main file
2. WHEN the latest file ID is determined, THE Download_Service SHALL call `GET /v1/games/{game_domain}/mods/{mod_id}/files/{file_id}/download_link.json` to obtain CDN URLs
3. WHEN CDN URLs are returned, THE Download_Service SHALL download the file to the RHI downloads folder and invoke the existing mod install flow
4. WHILE a download is in progress, THE GameCardViewModel SHALL display download progress (percentage) on the game card
5. IF the download link API returns a 403 (non-premium), THEN THE Download_Service SHALL fall back to the free-user flow for that download

### Requirement 6: Free User Download via nxm:// Protocol

**User Story:** As a free Nexus Mods user, I want RHI to guide me through downloading from the Nexus website and automatically catch the download link, so that I can still update mods with minimal friction.

#### Acceptance Criteria

1. WHEN a free user initiates a download, THE Download_Service SHALL open the mod's Nexus files page in the user's default browser
2. WHEN the user clicks "Download with Manager" on the Nexus website, THE Protocol_Handler SHALL intercept the nxm:// URI
3. WHEN an nxm:// URI is received, THE Protocol_Handler SHALL parse the game domain, mod ID, file ID, key, and expires parameters from the URI
4. WHEN valid key and expires parameters are extracted, THE Download_Service SHALL call the V1 download link API with those parameters to obtain CDN URLs
5. WHEN CDN URLs are obtained via nxm://, THE Download_Service SHALL download and install the file using the same flow as premium downloads
6. IF the nxm:// URI has an expired key (current time exceeds expires value), THEN THE Download_Service SHALL notify the user that the link has expired and prompt them to re-click on the Nexus website

### Requirement 7: nxm:// Protocol Handler Registration

**User Story:** As a user, I want RHI to register itself as the handler for nxm:// links, so that clicking "Download with Manager" on Nexus Mods routes the download to RHI.

#### Acceptance Criteria

1. WHEN the user enables Nexus integration in Settings, THE Protocol_Handler SHALL register RHI as the Windows handler for the `nxm` URI scheme via the Windows registry (`HKCU\Software\Classes\nxm`)
2. WHEN RHI is launched with an nxm:// URI as a command-line argument, THE Protocol_Handler SHALL parse and process the URI immediately
3. WHEN RHI is already running and receives an nxm:// URI via the single-instance forwarding mechanism, THE Protocol_Handler SHALL parse and process the URI on the running instance
4. WHEN the user disables Nexus integration or unlinks their account, THE Protocol_Handler SHALL remove the nxm registry entries
5. IF the registry write fails due to insufficient permissions, THEN THE Protocol_Handler SHALL notify the user that registration failed and suggest running as administrator

### Requirement 8: NXM URI Parsing

**User Story:** As a developer, I want a robust parser for nxm:// URIs, so that all valid Nexus download links are correctly interpreted.

#### Acceptance Criteria

1. THE Protocol_Handler SHALL parse URIs matching the pattern `nxm://{game_domain}/mods/{mod_id}/files/{file_id}?key={key}&expires={expires}`
2. THE Protocol_Handler SHALL extract game_domain as a non-empty string, mod_id and file_id as positive integers, key as a non-empty string, and expires as a positive integer (Unix timestamp)
3. IF the URI does not match the expected pattern, THEN THE Protocol_Handler SHALL log the malformed URI and discard it without crashing
4. FOR ALL valid NXM URIs, parsing then formatting back to a URI string then parsing again SHALL produce an equivalent result (round-trip property)

### Requirement 9: Token Storage Security

**User Story:** As a user, I want my Nexus Mods credentials stored securely, so that other applications cannot easily read my API key or tokens.

#### Acceptance Criteria

1. THE Token_Store SHALL encrypt API keys and OAuth tokens using the Windows Data Protection API (DPAPI) before writing to disk
2. THE Token_Store SHALL store encrypted tokens in `%LocalAppData%\RHI\nexus_tokens.dat`
3. WHEN reading tokens from disk, THE Token_Store SHALL decrypt using DPAPI and return the plaintext token to the Auth_Service
4. IF decryption fails (corrupted file or different user account), THEN THE Token_Store SHALL delete the corrupted file and return a null token, prompting re-authentication

### Requirement 10: Rate Limit Awareness

**User Story:** As a user, I want RHI to respect Nexus Mods API rate limits, so that my account is not penalized and the app remains functional throughout the day.

#### Acceptance Criteria

1. THE Nexus_API_Client SHALL track remaining daily requests from the `X-RL-Daily-Remaining` response header
2. WHEN the daily remaining count drops below 50, THE Nexus_API_Client SHALL log a warning and reduce update check frequency
3. IF the API returns HTTP 429 (Too Many Requests), THEN THE Nexus_API_Client SHALL pause all non-essential API calls and notify the user that the rate limit has been reached
4. THE Nexus_API_Client SHALL expose the current daily remaining count so the Settings UI can display it to the user

### Requirement 11: OAuth 2.0 + PKCE Authentication (Phase 2)

**User Story:** As a user of the public release, I want to authenticate with Nexus Mods via the standard OAuth flow, so that I do not need to manually copy API keys.

#### Acceptance Criteria

1. WHEN the user clicks "Link Nexus Account" in Settings, THE Auth_Service SHALL generate a PKCE code verifier (minimum 43 characters) and compute the SHA-256 code challenge
2. WHEN the PKCE challenge is generated, THE Auth_Service SHALL open the user's browser to the Nexus OAuth authorize URL with the code challenge, client ID, and redirect URI parameters
3. WHEN the OAuth flow is initiated, THE Auth_Service SHALL start a local HTTP listener on `http://127.0.0.1:{port}/callback` to receive the authorization code
4. WHEN the callback receives an authorization code, THE Auth_Service SHALL exchange it for access and refresh tokens at the Nexus token endpoint using the code verifier
5. WHEN tokens are received, THE Auth_Service SHALL store both access and refresh tokens in the Token_Store with their expiry times
6. WHEN the access token is within 5 minutes of expiry, THE Auth_Service SHALL use the refresh token to obtain a new access token without user interaction
7. IF the refresh token exchange fails, THEN THE Auth_Service SHALL clear stored tokens and prompt the user to re-authenticate
