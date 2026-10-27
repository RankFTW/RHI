# Bugfix Requirements Document

## Introduction

RDXC fails to detect updates for mods hosted on `clshortfuse.github.io` (including Generic Unity, Generic Unreal, and game-specific mods). The update check relies on comparing `Content-Length` from HTTP HEAD requests, but GitHub Pages CDN can return compressed transfer sizes that differ from the actual file size stored at install time. Additionally, `clshortfuse.github.io` URLs are not included in the download-based fallback set (`_downloadCheckUrls`), so they have no reliable alternative detection path. This causes users to miss mod updates silently.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a mod is hosted on `clshortfuse.github.io` and has been updated remotely THEN the system fails to detect the update because the HEAD request `Content-Length` may reflect a compressed transfer size that coincidentally matches (or mismatches unpredictably against) the stored `RemoteFileSize`, leading to false negatives

1.2 WHEN a mod is hosted on `clshortfuse.github.io` and has NOT been updated remotely THEN the system may incorrectly report an update available because the HEAD `Content-Length` (compressed) differs from the stored `RemoteFileSize` (uncompressed), leading to false positives

1.3 WHEN `clshortfuse.github.io` serves a HEAD response without a `Content-Length` header (e.g. chunked transfer encoding) THEN the system assumes no update is available and silently skips the check, even if the remote file has changed

### Expected Behavior (Correct)

2.1 WHEN a mod is hosted on `clshortfuse.github.io` and has been updated remotely THEN the system SHALL detect the update reliably by using download-based size comparison (the same strategy used for `marat569.github.io` URLs)

2.2 WHEN a mod is hosted on `clshortfuse.github.io` and has NOT been updated remotely THEN the system SHALL correctly report no update available, without false positives from compression-related size mismatches

2.3 WHEN `clshortfuse.github.io` serves a HEAD response without a `Content-Length` header THEN the system SHALL still detect updates by falling back to download-based comparison rather than assuming no update

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a mod is hosted on `marat569.github.io` THEN the system SHALL CONTINUE TO use download-based update detection as it does today

3.2 WHEN a mod is hosted on a non-GitHub-Pages URL that serves reliable `Content-Length` headers (e.g. GitHub Releases) THEN the system SHALL CONTINUE TO use HEAD-based Content-Length comparison for update detection

3.3 WHEN the local mod file is missing THEN the system SHALL CONTINUE TO return true (update/reinstall needed) regardless of the hosting URL

3.4 WHEN the installed mod record has no `SnapshotUrl` THEN the system SHALL CONTINUE TO return false (no update check possible)

---

### Bug Condition (Formal)

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type UpdateCheckInput  // (record.SnapshotUrl, localFileExists)
  OUTPUT: boolean

  // Returns true when the mod URL is a clshortfuse.github.io URL
  // (these are not in _downloadCheckUrls and rely on unreliable HEAD Content-Length)
  RETURN host(X.SnapshotUrl) = "clshortfuse.github.io"
END FUNCTION
```

```pascal
// Property: Fix Checking — clshortfuse.github.io mods use download-based detection
FOR ALL X WHERE isBugCondition(X) DO
  result ← CheckForUpdateAsync'(X)
  ASSERT update_detection_uses_download_comparison(result)
    AND no_false_negatives_from_compressed_content_length(result)
    AND no_false_positives_from_compressed_content_length(result)
END FOR
```

```pascal
// Property: Preservation Checking — non-clshortfuse URLs behave identically
FOR ALL X WHERE NOT isBugCondition(X) DO
  ASSERT CheckForUpdateAsync(X) = CheckForUpdateAsync'(X)
END FOR
```
