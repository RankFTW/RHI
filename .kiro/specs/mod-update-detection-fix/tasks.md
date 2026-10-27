# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** — clshortfuse.github.io URLs Use HEAD Instead of Download Path
  - **CRITICAL**: This test MUST FAIL on unfixed code — failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior — it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate `clshortfuse.github.io` URLs are routed through the unreliable HEAD Content-Length path instead of `CheckForUpdateByDownloadAsync`
  - **Scoped PBT Approach**: Use FsCheck to generate arbitrary `clshortfuse.github.io` URLs (varying path segments). For each, create an `InstalledModRecord` with that `SnapshotUrl`, a local file on disk, and a `RemoteFileSize`. Mock `HttpClient` so HEAD returns a compressed `Content-Length` that differs from stored `RemoteFileSize`, while GET returns the real (unchanged) file bytes. Assert that `CheckForUpdateAsync` returns `false` (no update) — on unfixed code, the HEAD path will see the mismatched compressed size and incorrectly return `true` (false positive), causing the property to fail
  - Bug condition from design: `isBugCondition(input) ≡ host(ResolveSnapshotUrl(input.SnapshotUrl)) = "clshortfuse.github.io"`
  - Expected behavior from design: clshortfuse URLs should route through `CheckForUpdateByDownloadAsync`, comparing real downloaded file sizes, producing correct results immune to compression artifacts
  - Create test file `RenoDXCommander.Tests/ModUpdateDetectionExplorationTests.cs`
  - Use `FsCheck.Xunit` `[Property]` attribute with a generator for `clshortfuse.github.io` URL paths
  - Use `FakeHttpMessageHandler` that returns compressed Content-Length on HEAD and real file bytes on GET
  - Instantiate `ModInstallService` with the fake `HttpClient`
  - Create temp directory with a local addon file matching the record
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (HEAD path returns incorrect result due to compressed Content-Length mismatch — this proves the bug exists)
  - Document counterexamples found (e.g., `clshortfuse.github.io/renodx/renodx-unityengine.addon64` with compressed HEAD size ≠ stored RemoteFileSize causes false positive)
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** — Non-clshortfuse URLs Behave Identically
  - **IMPORTANT**: Follow observation-first methodology
  - **IMPORTANT**: Write and verify these tests BEFORE implementing the fix
  - Observe on UNFIXED code:
    - `marat569.github.io` URLs route through `CheckForUpdateByDownloadAsync` (download-based detection)
    - Non-GitHub-Pages URLs (e.g. `github.com`, `example.com`) route through HEAD Content-Length comparison
    - Records with `SnapshotUrl = null` return `false` immediately
    - Records where the local file is missing return `true` immediately
  - Create test file `RenoDXCommander.Tests/ModUpdateDetectionPreservationTests.cs`
  - Write property-based tests using FsCheck:
    - **Property 2a**: For all `marat569.github.io` URL paths, `CheckForUpdateAsync` uses the download-based path (mock GET to return same-size file → returns `false`; different-size file → returns `true`). Verify by checking that the fake handler receives a GET (not just HEAD)
    - **Property 2b**: For all non-`github.io` host URLs (generate random hosts excluding `*.github.io`), `CheckForUpdateAsync` uses HEAD Content-Length comparison. Verify by checking that the fake handler receives only a HEAD request
    - **Property 2c**: For any `InstalledModRecord` with `SnapshotUrl = null`, `CheckForUpdateAsync` returns `false`
    - **Property 2d**: For any `InstalledModRecord` where the local file does not exist, `CheckForUpdateAsync` returns `true`
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: All preservation tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 3. Fix for clshortfuse.github.io update detection

  - [x] 3.1 Add `ShouldUseDownloadCheck` helper method
    - Add a `private static bool ShouldUseDownloadCheck(string url)` method to `ModInstallService.cs`
    - Parse the URL host using `new Uri(url).Host`
    - Return `true` when host ends with `".github.io"` (case-insensitive via `StringComparison.OrdinalIgnoreCase`)
    - Wrap in try/catch, return `false` for malformed URLs
    - _Bug_Condition: isBugCondition(input) where host(ResolveSnapshotUrl(input.SnapshotUrl)) = "clshortfuse.github.io" AND url NOT IN _downloadCheckUrls_
    - _Expected_Behavior: ShouldUseDownloadCheck returns true for all *.github.io hosts, routing them to CheckForUpdateByDownloadAsync_
    - _Preservation: Non-github.io URLs return false, preserving HEAD-based detection_
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2_

  - [x] 3.2 Replace `_downloadCheckUrls.Contains` with `ShouldUseDownloadCheck` in `CheckForUpdateAsync`
    - In `CheckForUpdateAsync`, replace `if (_downloadCheckUrls.Contains(checkUrl))` with `if (ShouldUseDownloadCheck(checkUrl))`
    - This routes all `*.github.io` URLs (including `clshortfuse.github.io`) through `CheckForUpdateByDownloadAsync`
    - _Bug_Condition: clshortfuse.github.io URLs were not in _downloadCheckUrls, so they fell through to unreliable HEAD path_
    - _Expected_Behavior: All *.github.io URLs now route to download-based detection_
    - _Preservation: Non-github.io URLs still take the HEAD path; marat569.github.io still takes the download path_
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2_

  - [x] 3.3 Remove or simplify `_downloadCheckUrls`
    - With host-based routing via `ShouldUseDownloadCheck`, the `_downloadCheckUrls` HashSet is no longer needed for GitHub Pages URLs
    - Either remove `_downloadCheckUrls` entirely, or keep it as a secondary override for future non-GitHub-Pages CDNs that may need download-based detection
    - Update the XML doc comment on `ShouldUseDownloadCheck` to explain the routing decision
    - _Preservation: Removing the HashSet has no behavioral impact since ShouldUseDownloadCheck covers all *.github.io hosts_
    - _Requirements: 2.1, 3.1_

  - [x] 3.4 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** — clshortfuse.github.io URLs Use Download Path
    - **IMPORTANT**: Re-run the SAME test from task 1 — do NOT write a new test
    - The test from task 1 encodes the expected behavior (clshortfuse URLs use download-based detection)
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed — clshortfuse URLs now route through CheckForUpdateByDownloadAsync)
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 3.5 Verify preservation tests still pass
    - **Property 2: Preservation** — Non-clshortfuse URLs Behave Identically
    - **IMPORTANT**: Re-run the SAME tests from task 2 — do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions — marat569 still uses download path, non-github.io still uses HEAD, null SnapshotUrl still returns false, missing file still returns true)
    - Confirm all tests still pass after fix (no regressions)

- [x] 4. Checkpoint — Ensure all tests pass
  - Run the full test suite to confirm no regressions
  - Ensure exploration test (Property 1) passes after fix
  - Ensure preservation tests (Property 2) pass after fix
  - Ensure any existing tests in the project still pass
  - Ask the user if questions arise
