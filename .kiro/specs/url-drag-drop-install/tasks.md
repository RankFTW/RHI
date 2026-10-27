# Implementation Plan: URL Drag-and-Drop Install

## Overview

Extend RHI's drag-and-drop infrastructure to accept URLs pointing to `.addon64`/`.addon32` files via two ingestion paths: OLE `IDropTarget` for text URLs dragged from browsers/Discord, and `.url` shortcut file detection in the existing `WM_DROPFILES` handler. Once a URL is captured, validate it, download to cache, PE-validate, and route through the existing `ProcessDroppedAddon` flow.

## Tasks

- [x] 1. Add OLE COM interfaces and P/Invoke declarations to NativeInterop.cs
  - [x] 1.1 Add OLE P/Invoke methods: `OleInitialize`, `RegisterDragDrop`, `RevokeDragDrop`
    - Add `[DllImport("ole32.dll")]` declarations for `OleInitialize(IntPtr)`, `RegisterDragDrop(IntPtr, IDropTarget)`, and `RevokeDragDrop(IntPtr)`
    - _Requirements: 1.1_

  - [x] 1.2 Add COM interface definitions for `IDropTarget`, `IDataObject`, and supporting structs
    - Define `IDropTarget` COM interface (`Guid("00000122-0000-0000-C000-000000000046")`) with `DragEnter`, `DragOver`, `DragLeave`, `Drop` methods
    - Define `IDataObject` COM interface (`Guid("0000010e-0000-0000-C000-000000000046")`) with `GetData`, `GetDataHere`, `QueryGetData`, `SetData` methods
    - Define `FORMATETC`, `STGMEDIUM`, `POINTL` structs and clipboard format constants (`CF_UNICODETEXT`, `CF_TEXT`, `CF_HDROP`)
    - Add `[DllImport("kernel32.dll")]` for `GlobalLock`, `GlobalUnlock`, `GlobalSize` needed to read `STGMEDIUM` data
    - _Requirements: 1.1_

- [x] 2. Implement OleDropTarget class in WindowStateManager
  - [x] 2.1 Create `OleDropTarget` inner class implementing `IDropTarget`
    - Implement `DragEnter`: check data object for `CF_UNICODETEXT` or `CF_HDROP`, set `DROPEFFECT_COPY` if present
    - Implement `DragOver`: return `DROPEFFECT_COPY` if drag was accepted in `DragEnter`
    - Implement `DragLeave`: no-op cleanup
    - Implement `Drop`: if `CF_HDROP` present, extract file paths and route through existing `HandleWin32Drop` logic (local files take priority per Req 1.3); otherwise extract text from `CF_UNICODETEXT`/`CF_TEXT`, check if it's a URL with addon extension, and route to `DragDropHandler.ProcessDroppedUrl`
    - Mark class `[ComVisible(true)]`
    - _Requirements: 1.1, 1.3_

  - [x] 2.2 Register OLE drop target in `EnableDragAccept()`
    - Call `OleInitialize(IntPtr.Zero)` before registration
    - Create `OleDropTarget` instance and call `RegisterDragDrop(_hwnd, oleDropTarget)`
    - Log and gracefully degrade if `RegisterDragDrop` fails (fall back to `WM_DROPFILES`-only)
    - _Requirements: 1.1_

  - [x] 2.3 Add `RevokeDragDrop` cleanup
    - Call `RevokeDragDrop(_hwnd)` during window close/cleanup (add to `MainWindow_Closed` or expose a cleanup method on `WindowStateManager`)
    - Log warning if revoke fails but continue shutdown
    - _Requirements: 1.1_

- [x] 3. Add `.url` shortcut file detection to WM_DROPFILES handler
  - [x] 3.1 Extend `HandleWin32Drop` to detect `.url` files
    - In the file path loop in `HandleWin32Drop`, add a check for `.url` extension before the existing extension checks
    - When a `.url` file is detected, read the file contents, parse the `[InternetShortcut]` section for the `URL=` line
    - If a valid URL is found, route to `DragDropHandler.ProcessDroppedUrl` instead of the file-based flow
    - If no URL is found in the `.url` file, log a warning and skip the file
    - _Requirements: 1.2_

- [x] 4. Checkpoint — Ensure OLE registration and `.url` detection compile and wire up
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Add URL processing to DragDropHandler
  - [x] 5.1 Add `ExtractFileNameFromUrl` static helper method
    - Parse the URL as a `System.Uri`, extract the `LocalPath` component
    - Get the filename from the last path segment via `Path.GetFileName`
    - URL-decode the filename via `Uri.UnescapeDataString`
    - Return `null` for unparseable URLs, empty paths, or paths with no filename
    - _Requirements: 2.1, 7.1, 7.2, 7.3, 7.4_

  - [x] 5.2 Add `ProcessDroppedUrl` async method
    - Validate the URL is parseable as a `System.Uri`; if not, log error and show error dialog (Req 2.4, 6.4)
    - Call `ExtractFileNameFromUrl` to get the addon filename
    - Check the filename has `.addon64` or `.addon32` extension (case-insensitive); reject with error dialog if not (Req 2.2, 2.3, 6.4)
    - Show a progress dialog indicating the addon filename being downloaded (Req 6.1)
    - Download to `%LocalAppData%\RHI\downloads\{filename}.tmp` using `HttpClient`, updating progress with bytes/percentage (Req 3.1, 3.2, 3.5, 6.2)
    - On network error or non-success HTTP status, delete `.tmp` file and show error dialog (Req 3.3)
    - On success, rename `.tmp` to final filename, overwriting if exists (Req 3.4, 3.5)
    - PE-validate the downloaded file by checking for `MZ` magic bytes; if invalid, delete and show error dialog (Req 4.1, 4.2)
    - Dismiss progress dialog and call existing `ProcessDroppedAddon(cachedFilePath)` (Req 4.3, 5.1, 5.2, 5.3, 6.3)
    - Wrap temp file cleanup in `finally` block
    - _Requirements: 2.1–2.4, 3.1–3.5, 4.1–4.3, 5.1–5.3, 6.1–6.4_

- [x] 6. Checkpoint — Ensure URL drop end-to-end flow compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Write property-based tests
  - [x] 7.1 Write property test for URL filename extraction correctness
    - **Property 1: URL filename extraction correctness**
    - Generate random filenames with `.addon64`/`.addon32` extensions, embed in random URL structures with random query params and fragments, verify `ExtractFileNameFromUrl` returns the original decoded filename
    - **Validates: Requirements 2.1, 7.1, 7.2, 7.3**

  - [x] 7.2 Write property test for addon URL acceptance by extension
    - **Property 2: Addon URL acceptance is determined by extension**
    - Generate random URLs with random extensions, verify acceptance matches `.addon64`/`.addon32` extension check (case-insensitive)
    - **Validates: Requirements 2.2, 2.3**

  - [x] 7.3 Write property test for extracted filename producing valid filesystem path
    - **Property 3: Extracted filename produces valid filesystem path**
    - Generate random addon URLs, extract filename, combine with a directory path, verify the resulting path has `.addon64` or `.addon32` extension
    - **Validates: Requirements 7.4**

  - [x] 7.4 Write property test for PE signature detection
    - **Property 4: PE signature detection**
    - Generate random byte arrays, verify `HasPeSignature`-equivalent logic returns true iff first two bytes are `0x4D` and `0x5A`, and false for arrays shorter than 2 bytes
    - **Validates: Requirements 4.1**

  - [x] 7.5 Write property test for `.url` shortcut file parsing round-trip
    - **Property 5: .url shortcut file parsing round-trip**
    - Generate random URL strings, write to `[InternetShortcut]\nURL={url}` format, parse back with the `.url` reader, verify round-trip equality
    - **Validates: Requirements 1.2**

- [x] 8. Write unit tests for edge cases
  - [x] 8.1 Write unit tests for `ExtractFileNameFromUrl`
    - Test Discord CDN URL with query params → returns correct filename
    - Test plain URL without query params → returns filename
    - Test `null`, empty string, garbage input → returns `null`
    - Test URL-encoded spaces (`%20`) → returns decoded filename
    - Test URL with fragment identifier (`#section`) → strips fragment
    - _Requirements: 2.1, 7.1, 7.2, 7.3_

  - [x] 8.2 Write unit tests for URL validation
    - Test `.addon64` and `.addon32` accepted (case-insensitive: `.ADDON64`, `.Addon32`)
    - Test `.exe`, `.dll`, `.zip` extensions rejected
    - Test URL with no extension rejected
    - _Requirements: 2.2, 2.3_

  - [x] 8.3 Write unit tests for `.url` file parsing
    - Test missing `[InternetShortcut]` section → returns `null`
    - Test empty `URL=` value → returns `null`
    - Test valid `.url` file → returns URL string
    - _Requirements: 1.2_

  - [x] 8.4 Write unit tests for PE validation edge cases
    - Test 0-byte file → returns false
    - Test 1-byte file → returns false
    - Test valid MZ header → returns true
    - _Requirements: 4.1_

- [x] 9. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The design uses C# throughout — all implementations target the existing WinUI 3 / .NET 8 project
- `HasPeSignature` already exists in `ModInstallService` as a private static method; the new code in `DragDropHandler` will replicate this pattern (or it can be extracted to a shared helper)
- OLE `IDropTarget` is required because WinUI 3 managed drag-drop doesn't reliably receive text data from external apps in unpackaged scenarios
- Local file paths always take priority over URL text when both are present in a drop operation
