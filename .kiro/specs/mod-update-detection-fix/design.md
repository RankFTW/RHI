# Mod Update Detection Fix — Bugfix Design

## Overview

RDXC's `CheckForUpdateAsync` uses HTTP HEAD `Content-Length` comparison to detect mod updates. This is unreliable for `clshortfuse.github.io` URLs because GitHub Pages CDN may return compressed transfer sizes or omit the header entirely. The fix changes the `_downloadCheckUrls` gate from an exact-URL match to a host-based check covering all `*.github.io` URLs, routing them through the existing `CheckForUpdateByDownloadAsync` path which downloads the actual file and compares real (uncompressed) sizes.

## Glossary

- **Bug_Condition (C)**: The mod's resolved `SnapshotUrl` has host `clshortfuse.github.io`, which is not in `_downloadCheckUrls`, causing it to use the unreliable HEAD Content-Length path
- **Property (P)**: All `clshortfuse.github.io` URLs use download-based comparison, producing correct update/no-update results immune to compression artifacts
- **Preservation**: All non-`clshortfuse.github.io` behavior remains identical — `marat569.github.io` still uses download-based detection, non-GitHub-Pages URLs still use HEAD-based detection, missing-file and no-SnapshotUrl early returns are unchanged
- **`CheckForUpdateAsync`**: Method in `ModInstallService.cs` (line 233) that determines whether a mod has a remote update available
- **`CheckForUpdateByDownloadAsync`**: Private method in `ModInstallService.cs` (line 300) that downloads the remote file to a temp path and compares its real size against the local file
- **`_downloadCheckUrls`**: Static `HashSet<string>` in `ModInstallService.cs` (line 54) listing URLs that should bypass HEAD and use download-based detection
- **`ResolveSnapshotUrl`**: Static method that maps addon filenames to authoritative URLs via `_addonUrlOverrides`

## Bug Details

### Bug Condition

The bug manifests when `CheckForUpdateAsync` is called for a mod whose resolved URL has host `clshortfuse.github.io`. Because `_downloadCheckUrls` only contains the single `marat569.github.io` URL, the `clshortfuse.github.io` URLs fall through to the HEAD-based path. GitHub Pages CDN may compress responses, return a `Content-Length` reflecting the compressed transfer size, or omit the header entirely — all of which produce incorrect update detection results.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type UpdateCheckInput  // { record: InstalledModRecord }
  OUTPUT: boolean

  resolvedUrl ← ResolveSnapshotUrl(input.record.SnapshotUrl)
  host ← extractHost(resolvedUrl)

  RETURN host = "clshortfuse.github.io"
         AND resolvedUrl NOT IN _downloadCheckUrls
END FUNCTION
```

### Examples

- **False negative**: Generic Unity mod installed from `https://clshortfuse.github.io/renodx/renodx-unityengine.addon64`. Remote file updated (size changed from 50000 to 52000 bytes). HEAD returns `Content-Length: 50000` (compressed size matches stored `RemoteFileSize`). Result: no update detected. Expected: update detected.
- **False positive**: Same URL, remote file NOT updated. HEAD returns `Content-Length: 48500` (compressed size differs from stored 50000). Result: update reported. Expected: no update.
- **Missing header**: Same URL, HEAD response uses chunked transfer encoding with no `Content-Length`. Code returns `false` (no update). Expected: download-based fallback detects the actual change.
- **Edge case — `marat569.github.io`**: URL `https://marat569.github.io/renodx/renodx-ue-extended.addon64` is already in `_downloadCheckUrls`. This works correctly today and must continue to work.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- `marat569.github.io` URLs continue to use download-based update detection exactly as today
- Non-GitHub-Pages URLs (e.g. GitHub Releases, other CDNs) continue to use HEAD Content-Length comparison
- When the local mod file is missing, `CheckForUpdateAsync` returns `true` regardless of URL
- When `record.SnapshotUrl` is null, `CheckForUpdateAsync` returns `false`
- `CheckForUpdateByDownloadAsync` internal logic (download, compare, cache, save record) is unchanged
- `ResolveSnapshotUrl` and `_addonUrlOverrides` behavior is unchanged
- `UpdateOrchestrationService.CheckForUpdatesAsync` orchestration flow is unchanged

**Scope:**
All inputs whose resolved URL host is NOT `clshortfuse.github.io` (and not already in `_downloadCheckUrls`) should be completely unaffected by this fix. This includes:
- URLs on `github.com` (GitHub Releases)
- URLs on other CDNs or custom domains
- The existing `marat569.github.io` download-check path
- All early-return paths (null SnapshotUrl, missing local file)

## Hypothesized Root Cause

Based on the bug description and code analysis, the root cause is:

1. **Incomplete `_downloadCheckUrls` set**: The `_downloadCheckUrls` HashSet only contains the single `marat569.github.io` URL. When the same CDN reliability issue affects `clshortfuse.github.io` URLs, they are not routed to the download-based path. This is the primary and most likely root cause — a simple omission.

2. **Exact-URL matching instead of host-based matching**: The current design uses `_downloadCheckUrls.Contains(checkUrl)` which requires every individual URL to be listed. A host-based check (e.g. `url.Host.EndsWith(".github.io")`) would be more robust and future-proof, catching any new GitHub Pages URLs automatically.

3. **GitHub Pages CDN compression behavior**: GitHub Pages applies transparent compression (gzip/brotli) and the `Content-Length` header in HEAD responses may reflect the compressed transfer size rather than the actual file size. This is a known CDN behavior, not a bug in GitHub Pages itself — the code simply needs to account for it.

## Correctness Properties

Property 1: Bug Condition — clshortfuse.github.io mods use download-based detection

_For any_ input where the mod's resolved SnapshotUrl has host `clshortfuse.github.io` (isBugCondition returns true), the fixed `CheckForUpdateAsync` SHALL route the update check through `CheckForUpdateByDownloadAsync`, comparing real downloaded file sizes rather than HEAD Content-Length, thereby producing correct update detection results immune to compression artifacts.

**Validates: Requirements 2.1, 2.2, 2.3**

Property 2: Preservation — non-clshortfuse URLs behave identically

_For any_ input where the mod's resolved SnapshotUrl host is NOT `clshortfuse.github.io` (isBugCondition returns false), the fixed `CheckForUpdateAsync` SHALL produce the same result as the original function, preserving HEAD-based detection for reliable CDNs and download-based detection for `marat569.github.io`.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct (incomplete `_downloadCheckUrls` — hypothesis 1/2):

**File**: `RenoDXCommander/Services/ModInstallService.cs`

**Function**: `CheckForUpdateAsync` (line 233) and `_downloadCheckUrls` (line 54)

**Specific Changes**:

1. **Replace exact-URL set with host-based check**: Replace the `_downloadCheckUrls.Contains(checkUrl)` check with a helper method that parses the URL host and checks if it ends with `.github.io`. This covers both `marat569.github.io` and `clshortfuse.github.io` (and any future GitHub Pages hosts) in one check.

   - Option A (minimal): Add the three `clshortfuse.github.io` URLs to `_downloadCheckUrls`. Simple but requires updating the set whenever new URLs are added.
   - Option B (recommended): Replace the `HashSet` check with a host-based predicate like `ShouldUseDownloadCheck(string url)` that returns `true` when `new Uri(url).Host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase)`. Remove or keep `_downloadCheckUrls` as a secondary override set.

2. **Add `ShouldUseDownloadCheck` helper method**: Create a private static method that encapsulates the routing decision:
   ```csharp
   private static bool ShouldUseDownloadCheck(string url)
   {
       try
       {
           var host = new Uri(url).Host;
           return host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase);
       }
       catch { return false; }
   }
   ```

3. **Update `CheckForUpdateAsync` call site**: Replace `_downloadCheckUrls.Contains(checkUrl)` with `ShouldUseDownloadCheck(checkUrl)` at the routing decision point (around line 256).

4. **Remove or simplify `_downloadCheckUrls`**: With host-based routing, the explicit URL set is no longer needed for GitHub Pages URLs. It can be removed entirely or retained as an additional override mechanism for non-GitHub-Pages CDNs that may need download-based detection in the future.

5. **No changes to `CheckForUpdateByDownloadAsync`**: The download-based detection logic is already correct and handles all the comparison, caching, and record-saving. No modifications needed.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that call `CheckForUpdateAsync` with `InstalledModRecord` instances whose `SnapshotUrl` points to `clshortfuse.github.io`. Mock the HTTP client to simulate GitHub Pages CDN behavior (compressed Content-Length, missing headers). Run these tests on the UNFIXED code to observe that the HEAD-based path is taken instead of the download-based path.

**Test Cases**:
1. **Generic Unity URL test**: Create record with `SnapshotUrl = "https://clshortfuse.github.io/renodx/renodx-unityengine.addon64"`, mock HEAD to return compressed Content-Length matching stored `RemoteFileSize`. Assert that update is NOT detected (will demonstrate false negative on unfixed code).
2. **Generic Unreal URL test**: Create record with `SnapshotUrl = "https://clshortfuse.github.io/renodx/renodx-unrealengine.addon64"`, mock HEAD to return different compressed Content-Length. Assert that false positive occurs (will demonstrate false positive on unfixed code).
3. **Missing Content-Length test**: Create record with `clshortfuse.github.io` URL, mock HEAD to return no Content-Length header. Assert that `false` is returned even though remote file changed (will demonstrate silent skip on unfixed code).
4. **Routing verification**: Assert that `_downloadCheckUrls.Contains(resolvedUrl)` returns `false` for `clshortfuse.github.io` URLs on unfixed code, confirming the root cause.

**Expected Counterexamples**:
- `clshortfuse.github.io` URLs take the HEAD-based path instead of download-based path
- Compressed Content-Length values cause incorrect comparison results
- Possible causes: missing URL in `_downloadCheckUrls` (confirmed by code inspection)

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result ← CheckForUpdateAsync_fixed(input)
  ASSERT update_detection_used_download_path(result)
    AND correct_update_status_returned(result)
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT CheckForUpdateAsync(input) = CheckForUpdateAsync_fixed(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the URL domain (various hosts, paths, query strings)
- It catches edge cases like URLs that partially match `.github.io` but shouldn't (e.g. `notgithub.io`)
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for non-`clshortfuse.github.io` URLs, then write property-based tests capturing that behavior.

**Test Cases**:
1. **marat569 preservation**: Verify `marat569.github.io` URLs continue to route through `CheckForUpdateByDownloadAsync` after the fix
2. **GitHub Releases preservation**: Verify `github.com` URLs continue to use HEAD-based Content-Length comparison
3. **Null SnapshotUrl preservation**: Verify records with null `SnapshotUrl` still return `false`
4. **Missing local file preservation**: Verify records with missing local files still return `true`

### Unit Tests

- Test `ShouldUseDownloadCheck` returns `true` for `clshortfuse.github.io` URLs
- Test `ShouldUseDownloadCheck` returns `true` for `marat569.github.io` URLs
- Test `ShouldUseDownloadCheck` returns `false` for `github.com` URLs
- Test `ShouldUseDownloadCheck` returns `false` for malformed URLs (graceful fallback)
- Test `CheckForUpdateAsync` routes `clshortfuse.github.io` URLs to download-based path
- Test early returns (null SnapshotUrl → false, missing file → true) are unchanged

### Property-Based Tests

- Generate random `*.github.io` URLs and verify they all route to download-based detection
- Generate random non-`github.io` URLs and verify they route to HEAD-based detection
- Generate random `InstalledModRecord` instances with various URL hosts and verify routing correctness
- Test that `ShouldUseDownloadCheck` is consistent with the old `_downloadCheckUrls` behavior for all previously-listed URLs

### Integration Tests

- Test full `CheckForUpdatesAsync` orchestration flow with a mix of `clshortfuse.github.io` and non-GitHub-Pages mods
- Test that update detection correctly identifies a real update for a `clshortfuse.github.io` mod (mocked download returns different size)
- Test that update detection correctly reports no update for a `clshortfuse.github.io` mod (mocked download returns same size)
