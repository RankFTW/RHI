# Implementation Plan: Nexus API Integration

## Overview

This plan implements Nexus Mods API integration for RHI in incremental steps, starting with data models and pure-logic utilities, then building up through token storage, authentication, API client, update checking, downloads, and protocol handling. Each step builds on the previous and wires into the existing DI container. OAuth (Requirement 11) is Phase 2 and marked optional. Property-based tests validate the 10 correctness properties from the design document.

## Tasks

- [x] 1. Create data model records and URL parsing utility
  - [x] 1.1 Create Nexus data model records
    - Create `RenoDXCommander/Models/NexusUserProfile.cs` with the `NexusUserProfile` record
    - Create `RenoDXCommander/Models/NexusValidationResponse.cs` with the `NexusValidationResponse` record
    - Create `RenoDXCommander/Models/NexusFileInfo.cs` with the `NexusFileInfo` record
    - Create `RenoDXCommander/Models/NexusDownloadLink.cs` with the `NexusDownloadLink` record
    - Create `RenoDXCommander/Models/NexusModReference.cs` with the `NexusModReference` record
    - Create `RenoDXCommander/Models/NxmUri.cs` with the `NxmUri` record
    - Create `RenoDXCommander/Models/OAuthTokenData.cs` with the `OAuthTokenData` record
    - Create `RenoDXCommander/Models/NexusUpdateResult.cs` with the `NexusUpdateResult` record
    - _Requirements: 2.1, 2.2, 3.2, 5.1, 6.3, 8.1, 8.2, 9.1, 11.5_

  - [x] 1.2 Implement NexusUrlParser static utility
    - Create `RenoDXCommander/Services/NexusUrlParser.cs` with `Parse` and `Format` methods
    - Support both URL formats: `/{game_domain}/mods/{mod_id}` and `/games/{game_domain}`
    - Return `NexusModReference` with nullable `ModId` for catalogue-only URLs
    - Handle edge cases: trailing slashes, query strings, fragments, empty/null input
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 1.3 Write property test for NexusUrlParser round-trip
    - **Property 1: Nexus URL parse round-trip**
    - Generate random `NexusModReference` values (lowercase alphanumeric domain, optional positive mod ID)
    - Verify `Parse(Format(ref))` produces an equivalent `NexusModReference`
    - Create `RenoDXCommander.Tests/NexusUrlParseRoundTripPropertyTests.cs`
    - **Validates: Requirements 2.1, 2.2, 2.4**

  - [x] 1.4 Write unit tests for NexusUrlParser
    - Test concrete URL examples for both formats
    - Test edge cases: trailing slashes, query strings, fragments, empty strings, null, malformed URLs
    - Create `RenoDXCommander.Tests/NexusUrlParserUnitTests.cs`
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 2. Implement token storage with DPAPI encryption
  - [x] 2.1 Create INexusTokenStore interface and NexusTokenStore implementation
    - Create `RenoDXCommander/Services/INexusTokenStore.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NexusTokenStore.cs` implementing DPAPI encryption via `System.Security.Cryptography.ProtectedData`
    - Store encrypted tokens at `%LocalAppData%\RHI\nexus_tokens.dat`
    - Handle corrupted file by deleting and returning null
    - Implement `SaveApiKey`, `LoadApiKey`, `SaveOAuthTokens`, `LoadOAuthTokens`, `ClearAll`, `HasStoredToken`
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 2.2 Write property test for token store encryption round-trip
    - **Property 5: Token store encryption round-trip**
    - Generate random non-empty token strings
    - Verify `LoadApiKey()` after `SaveApiKey(token)` returns the original token
    - Use a temp directory to avoid polluting real app data
    - Create `RenoDXCommander.Tests/NexusTokenStoreRoundTripPropertyTests.cs`
    - **Validates: Requirements 9.1, 9.3**

  - [x] 2.3 Write unit tests for NexusTokenStore
    - Test corrupted file handling (write garbage bytes, verify LoadApiKey returns null and file is deleted)
    - Test ClearAll removes the file
    - Test HasStoredToken returns correct state
    - Create `RenoDXCommander.Tests/NexusTokenStoreUnitTests.cs`
    - _Requirements: 9.2, 9.4_

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement Nexus API client with rate limit tracking
  - [x] 4.1 Create INexusApiClient interface and NexusApiClient implementation
    - Create `RenoDXCommander/Services/INexusApiClient.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NexusApiClient.cs` implementing:
      - `ValidateKeyAsync` calling `GET /v1/users/validate.json` with the API key as Bearer token
      - `GetModFilesGraphQLAsync` calling `POST /v2/graphql` with a modFiles query
      - `GetModFilesV1Async` calling `GET /v1/games/{domain}/mods/{id}/files.json`
      - `GetDownloadLinksAsync` calling `GET /v1/.../download_link.json` with optional nxm key+expires params
    - Include `Application-Name: RHI` and `Application-Version: {version}` headers on every request
    - Track `X-RL-Daily-Remaining` from response headers, expose `DailyRequestsRemaining` and `IsRateLimited`
    - Set `IsRateLimited = true` on HTTP 429, stop non-essential calls
    - Log warning when daily remaining drops below 50
    - _Requirements: 1.1, 1.5, 3.1, 5.1, 5.2, 10.1, 10.2, 10.3, 10.4_

  - [x] 4.2 Write property test for rate limit header tracking
    - **Property 10: Rate limit header tracking**
    - Generate random non-negative integers for the `X-RL-Daily-Remaining` header value
    - Verify `DailyRequestsRemaining` equals the header value and `IsRateLimited` is false when value > 0
    - Create `RenoDXCommander.Tests/NexusRateLimitTrackingPropertyTests.cs`
    - **Validates: Requirements 10.1, 10.2**

- [x] 5. Implement authentication service
  - [x] 5.1 Create INexusAuthService interface and NexusAuthService implementation
    - Create `RenoDXCommander/Services/INexusAuthService.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NexusAuthService.cs` implementing:
      - `ValidateAndStoreApiKeyAsync`: call `INexusApiClient.ValidateKeyAsync`, store key on success, extract premium status
      - `RevalidateStoredKeyAsync`: load key from token store, re-validate in background at startup
      - `UnlinkAccount`: call `INexusTokenStore.ClearAll`, clear cached `CurrentUser`
      - `GetBearerToken`: return stored API key (Phase 1) or access token (Phase 2)
      - `IsAuthenticated`, `IsPremium`, `CurrentUser` properties
    - Handle 401 responses by not storing the key and surfacing an error
    - Stub `StartOAuthFlowAsync` and `RefreshTokenIfNeededAsync` for Phase 2
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [x] 5.2 Write unit tests for NexusAuthService
    - Mock INexusApiClient and INexusTokenStore
    - Test successful validation stores key and sets IsPremium
    - Test 401 response does not store key
    - Test UnlinkAccount clears tokens and profile
    - Test RevalidateStoredKeyAsync re-validates on startup
    - Create `RenoDXCommander.Tests/NexusAuthServiceUnitTests.cs`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6_

- [x] 6. Implement NXM protocol handler and URI parsing
  - [x] 6.1 Create INxmProtocolHandler interface and NxmProtocolHandler implementation
    - Create `RenoDXCommander/Services/INxmProtocolHandler.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NxmProtocolHandler.cs` implementing:
      - `Parse`: extract game domain, mod ID, file ID, key, expires from `nxm://{domain}/mods/{modId}/files/{fileId}?key={key}&expires={expires}`
      - `Format`: reconstruct a valid nxm:// URI string from an `NxmUri`
      - `Register`: write `HKCU\Software\Classes\nxm` registry entries pointing to RHI executable
      - `Unregister`: remove the nxm registry entries
      - `IsRegistered`: check if RHI is currently the registered handler
    - Parse must never throw — return null for malformed input, log the raw URI
    - Handle registry write failures by catching `UnauthorizedAccessException` and notifying user
    - _Requirements: 7.1, 7.2, 7.4, 7.5, 8.1, 8.2, 8.3_

  - [x] 6.2 Write property test for NXM URI parse round-trip
    - **Property 2: NXM URI parse round-trip**
    - Generate random valid `NxmUri` values (alphanumeric domain, positive IDs, non-empty key, positive expires)
    - Verify `Parse(Format(uri))` produces an equivalent `NxmUri`
    - Create `RenoDXCommander.Tests/NxmUriParseRoundTripPropertyTests.cs`
    - **Validates: Requirements 8.1, 8.2, 8.4, 6.3**

  - [x] 6.3 Write property test for NXM URI parsing robustness
    - **Property 3: NXM URI parsing robustness**
    - Generate arbitrary strings, verify `Parse` never throws an exception
    - Returns valid `NxmUri` for well-formed input or `null` for malformed input
    - Create `RenoDXCommander.Tests/NxmUriParsingRobustnessPropertyTests.cs`
    - **Validates: Requirements 8.3**

  - [x] 6.4 Write property test for NXM link expiry detection
    - **Property 4: NXM link expiry detection**
    - Generate random `(DateTimeOffset currentTime, long expires)` pairs
    - Verify `isExpired == (currentTime.ToUnixTimeSeconds() >= expires)`
    - Create `RenoDXCommander.Tests/NxmLinkExpiryDetectionPropertyTests.cs`
    - **Validates: Requirements 6.6**

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement update checker with caching
  - [x] 8.1 Create INexusUpdateChecker interface and NexusUpdateChecker implementation
    - Create `RenoDXCommander/Services/INexusUpdateChecker.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NexusUpdateChecker.cs` implementing:
      - `CheckAllAsync`: iterate cards, use `NexusUrlParser.Parse` to resolve game domain + mod ID from `GameMod.NexusUrl`, query V2 GraphQL for latest file, compare versions
      - `CheckSingleAsync`: check a single mod, return `NexusUpdateResult`
      - In-memory cache with configurable TTL (default 1 hour), keyed by `(gameDomain, modId)`
      - `ClearCache`: wipe the in-memory cache
      - `bypassCache` parameter to force re-query on manual refresh
      - Skip mods where NexusUrl is null or unparseable (log warning)
      - Stop on HTTP 429 (rate limit), log the condition
      - Set `IsNexusUpdateAvailable` on `GameCardViewModel` when remote version differs from local
      - Run off the UI thread (async, no blocking)
    - _Requirements: 2.1, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 8.2 Write property test for version comparison
    - **Property 8: Version comparison determines update availability**
    - Generate random pairs of non-empty version strings
    - Verify `updateAvailable == (localVersion != remoteVersion)`
    - Create `RenoDXCommander.Tests/NexusVersionComparisonPropertyTests.cs`
    - **Validates: Requirements 3.2**

  - [x] 8.3 Write property test for cache TTL decision
    - **Property 9: Cache TTL decision**
    - Generate random `(DateTime lastCheckUtc, DateTime currentUtc, TimeSpan ttl)` triples
    - Verify cache reports hit if and only if `(currentUtc - lastCheckUtc) < ttl`
    - Create `RenoDXCommander.Tests/NexusCacheTtlDecisionPropertyTests.cs`
    - **Validates: Requirements 3.4**

  - [x] 8.4 Write unit tests for NexusUpdateChecker
    - Mock INexusApiClient, test update detection with version mismatch
    - Test cache bypass on manual refresh
    - Test rate limit handling (stop on 429)
    - Test skipping mods with null/unparseable NexusUrl
    - Create `RenoDXCommander.Tests/NexusUpdateCheckerUnitTests.cs`
    - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.6_

- [x] 9. Implement download service for premium and free users
  - [x] 9.1 Create INexusDownloadService interface and NexusDownloadService implementation
    - Create `RenoDXCommander/Services/INexusDownloadService.cs` with the interface from the design
    - Create `RenoDXCommander/Services/NexusDownloadService.cs` implementing:
      - `DownloadAndInstallAsync`: check `INexusAuthService.IsPremium`, choose premium or free flow
      - Premium flow: call `GetModFilesV1Async` to find latest main file, call `GetDownloadLinksAsync` for CDN URLs, download to RHI downloads folder, invoke existing mod install flow
      - Free flow: open mod's Nexus files page in default browser via `Process.Start`
      - `ProcessNxmUriAsync`: parse nxm URI, check expiry, call `GetDownloadLinksAsync` with key+expires, download and install
      - Report download progress via `IProgress<double>` and update `GameCardViewModel.NexusDownloadProgress`
      - Handle 403 on download link by falling back to free-user flow
      - Handle expired nxm key by notifying user
      - Support `CancellationToken` for download cancellation
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.4, 6.5, 6.6_

  - [x] 9.2 Write unit tests for NexusDownloadService
    - Mock INexusApiClient and INexusAuthService
    - Test premium flow calls correct API sequence
    - Test 403 fallback to free-user flow
    - Test nxm URI processing with valid and expired keys
    - Test progress reporting
    - Create `RenoDXCommander.Tests/NexusDownloadServiceUnitTests.cs`
    - _Requirements: 5.1, 5.2, 5.3, 5.5, 6.4, 6.5, 6.6_

- [x] 10. Extend GameCardViewModel with Nexus state properties
  - [x] 10.1 Create GameCardViewModel.Nexus.cs partial class
    - Create `RenoDXCommander/ViewModels/GameCardViewModel.Nexus.cs` as a partial class
    - Add `[ObservableProperty] private bool _isNexusUpdateAvailable;`
    - Add `[ObservableProperty] private bool _isNexusDownloading;`
    - Add `[ObservableProperty] private double _nexusDownloadProgress;`
    - Add `[ObservableProperty] private string _nexusActionMessage = "";`
    - Add computed properties for badge visibility (e.g., `NexusUpdateBadgeVisibility` based on `IsNexusUpdateAvailable` and auth state)
    - Update `NotifyAll()` in the main partial to include the new Nexus properties
    - _Requirements: 3.7, 4.1, 4.3, 5.4_

  - [x] 10.2 Write unit tests for GameCardViewModel Nexus properties
    - Test badge visibility when `IsNexusUpdateAvailable` is true/false
    - Test badge hidden when no API key is linked
    - Create `RenoDXCommander.Tests/NexusGameCardPropertyTests.cs`
    - _Requirements: 4.1, 4.3_

- [x] 11. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Wire services into DI container and integrate with app lifecycle
  - [x] 12.1 Register new services in App.xaml.cs
    - Add `System.Security.Cryptography.ProtectedData` NuGet package to `RenoDXCommander.csproj` if not already present
    - Register `INexusTokenStore` → `NexusTokenStore` as singleton
    - Register `INexusApiClient` → `NexusApiClient` as singleton
    - Register `INexusAuthService` → `NexusAuthService` as singleton
    - Register `INexusUpdateChecker` → `NexusUpdateChecker` as singleton
    - Register `INexusDownloadService` → `NexusDownloadService` as singleton
    - Register `INxmProtocolHandler` → `NxmProtocolHandler` as singleton
    - _Requirements: 1.1, 1.5_

  - [x] 12.2 Integrate nxm:// protocol handling with single-instance service
    - In `App.OnLaunched`, detect `nxm://` command-line arguments alongside existing addon file detection
    - Forward nxm:// URIs to `INxmProtocolHandler.Parse` → `INexusDownloadService.ProcessNxmUriAsync`
    - Wire `SingleInstanceService.FileReceived` (or add a new event) to handle nxm:// URIs received by the running instance
    - _Requirements: 7.2, 7.3_

  - [x] 12.3 Integrate startup re-validation and update checks
    - After `MainWindow` activates, call `INexusAuthService.RevalidateStoredKeyAsync` in background
    - After auth validation, trigger `INexusUpdateChecker.CheckAllAsync` for all game cards with Nexus URLs
    - Wire manual refresh button to call `CheckAllAsync(bypassCache: true)`
    - _Requirements: 1.6, 3.1, 3.5_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 14. OAuth 2.0 + PKCE authentication (Phase 2 — optional)
  - [ ] 14.1 Implement OAuth flow in NexusAuthService
    - Implement `StartOAuthFlowAsync`: generate PKCE code verifier (≥43 chars) and SHA-256 code challenge
    - Open browser to Nexus OAuth authorize URL with code challenge, client ID, redirect URI
    - Start local HTTP listener on `http://127.0.0.1:{port}/callback` to receive authorization code
    - Exchange authorization code for access + refresh tokens at Nexus token endpoint
    - Store tokens via `INexusTokenStore.SaveOAuthTokens`
    - Implement `RefreshTokenIfNeededAsync`: refresh when access token is within 5 minutes of expiry
    - Handle refresh failure by clearing tokens and prompting re-authentication
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_

  - [ ] 14.2 Write property test for PKCE code challenge correctness
    - **Property 6: PKCE code challenge correctness**
    - Generate random code verifier strings (≥43 chars, unreserved characters)
    - Verify computed challenge equals `Base64Url(SHA256(verifier))` with no padding
    - Create `RenoDXCommander.Tests/NexusPkceCodeChallengePropertyTests.cs`
    - **Validates: Requirements 11.1**

  - [ ] 14.3 Write property test for token refresh threshold decision
    - **Property 7: Token refresh threshold decision**
    - Generate random `(DateTimeOffset currentTime, DateTimeOffset tokenExpiry)` pairs
    - Verify `needsRefresh == (tokenExpiry - currentTime <= TimeSpan.FromMinutes(5))`
    - Create `RenoDXCommander.Tests/NexusTokenRefreshThresholdPropertyTests.cs`
    - **Validates: Requirements 11.6**

  - [ ] 14.4 Write unit tests for OAuth flow
    - Test PKCE verifier generation meets minimum length
    - Test code challenge computation
    - Test token refresh logic near expiry boundary
    - Test refresh failure clears tokens
    - Create `RenoDXCommander.Tests/NexusOAuthFlowUnitTests.cs`
    - _Requirements: 11.1, 11.4, 11.6, 11.7_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate the 10 universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Task 14 (OAuth) is Phase 2 — build with personal API key first for testing, then swap to OAuth for public release
- The project uses `InternalsVisibleTo` for the test project, so `internal` helpers are testable
- All new services follow the existing interface + implementation + DI singleton pattern
