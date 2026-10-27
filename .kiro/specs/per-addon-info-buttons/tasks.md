# Implementation Plan: Per-Addon Info Buttons

## Overview

Add per-addon "Info" buttons to every component row across all view modes (Detail, Card flyout, Compact). Each button opens a ContentDialog showing contextual information resolved through a three-tier priority system: per-game manifest entries → wiki-scraped content (RenoDX/OptiScaler/Luma) → generic fallback text. Remove the existing header-level "ℹ" and "💬" buttons and migrate their functionality to the per-addon RenoDX Info button.

## Tasks

- [x] 1. Create data models and enums
  - [x] 1.1 Create AddonType enum and InfoSourceType enum in `RenoDXCommander/Models/AddonType.cs`
    - Define `AddonType` enum: REFramework, ReShade, RenoDX, ReLimiter, DisplayCommander, OptiScaler, Luma
    - Define `InfoSourceType` enum: None, Manifest, Wiki, Fallback
    - _Requirements: 4.1–4.5_

  - [x] 1.2 Create AddonInfoResult model in `RenoDXCommander/Models/AddonInfoResult.cs`
    - Properties: Content, Url, UrlLabel, Source (InfoSourceType)
    - RenoDX-specific: WikiStatusLabel, WikiStatusBadgeBg, WikiStatusBadgeFg, WikiStatusBadgeBorder
    - OptiScaler-specific: OptiScalerCompat, OptiScalerFsr4Compat
    - _Requirements: 4.1–4.5, 5.4, 7.3_

  - [x] 1.3 Create OptiScalerCompatEntry and OptiScalerWikiData models in `RenoDXCommander/Models/OptiScalerWikiData.cs`
    - OptiScalerCompatEntry: GameName, Status, Upscalers (List<string>), Notes, DetailPageUrl
    - OptiScalerWikiData: StandardCompat dictionary, Fsr4Compat dictionary (both case-insensitive)
    - _Requirements: 7.1–7.4, 15.1–15.4_

  - [x] 1.4 Extend RemoteManifest model with per-addon info dictionary properties
    - Add nullable Dictionary<string, GameNoteEntry> properties: ReshadeGameInfo, RelimiterGameInfo, DisplayCommanderGameInfo, ReframeworkGameInfo, OptiScalerGameInfo, LumaGameInfo
    - Add nullable Dictionary<string, string> OptiScalerWikiNames property
    - Add JsonPropertyName attributes matching design spec
    - _Requirements: 6.1–6.4_

- [x] 2. Implement AddonInfoResolver service
  - [x] 2.1 Create AddonInfoResolver class in `RenoDXCommander/Services/AddonInfoResolver.cs`
    - Implement `Resolve(GameCardViewModel card, AddonType addon, RemoteManifest? manifest, OptiScalerWikiData? osWikiData)` method
    - Tier 1: Check per-addon manifest dictionary for the game name
    - Tier 2: Check wiki content (RenoDX wiki for RenoDX, OptiScaler wiki for OptiScaler, Luma wiki/manifest for Luma)
    - Tier 3: Return static generic fallback text
    - Implement `GetTooltip()` returning "Per-game notes available" / "Wiki info available" / "General addon info"
    - Implement `GetSourceType()` returning the InfoSourceType for styling decisions
    - _Requirements: 4.1–4.7, 9.1–9.8, 12.1–12.4_

  - [x] 2.2 Define generic fallback text constants in AddonInfoResolver
    - RE Framework: modding framework for RE Engine games
    - ReShade: post-processing injector required by RenoDX
    - RenoDX: HDR mod framework using ReShade
    - ReLimiter: frame limiter for input lag reduction
    - Display Commander: display management for HDR output
    - OptiScaler: upscaling compatibility layer for FSR/XeSS/DLSS
    - Luma: alternative HDR mod with native output
    - _Requirements: 9.1–9.8_

  - [x] 2.3 Write property test for manifest priority (Property 1)
    - **Property 1: Manifest entry is highest priority**
    - **Validates: Requirements 4.1, 4.2**

  - [x] 2.4 Write property test for wiki tier 2 (Property 2)
    - **Property 2: Wiki content is tier 2 for eligible addons**
    - **Validates: Requirements 4.3, 4.4, 4.7**

  - [x] 2.5 Write property test for generic fallback (Property 3)
    - **Property 3: Generic fallback is tier 3**
    - **Validates: Requirements 4.5, 9.1–9.8**

  - [x] 2.6 Write property test for independent resolution (Property 4)
    - **Property 4: Resolution is independent per addon**
    - **Validates: Requirements 4.6**

  - [x] 2.7 Write property test for tooltip/style from source (Property 8)
    - **Property 8: Tooltip and visual style are determined by source type**
    - **Validates: Requirements 11.1, 11.2, 12.1, 12.2, 12.3, 12.4**

- [x] 3. Implement OptiScaler wiki scraping
  - [x] 3.1 Create IOptiScalerWikiService interface and OptiScalerWikiService class in `RenoDXCommander/Services/OptiScalerWikiService.cs`
    - Implement `FetchAsync(IProgress<string>? progress)` to fetch both Compatibility List and FSR4 Compatibility List pages
    - Parse HTML tables to extract game entries with status, upscalers, notes, and detail page URLs
    - Merge both lists into a single OptiScalerWikiData with case-insensitive dictionaries
    - Implement session caching via `CachedData` property
    - Use defensive HTML parsing with null checks; log parse failures via CrashReporter
    - _Requirements: 7.1–7.6, 13.1–13.4, 15.1–15.4_

  - [x] 3.2 Integrate OptiScaler wiki fetch into application startup
    - Fetch OptiScaler wiki data in parallel with existing RenoDX wiki fetch
    - Handle fetch failures gracefully (log and continue with empty data)
    - Store cached data accessible to AddonInfoResolver
    - _Requirements: 13.1–13.5_

  - [x] 3.3 Implement OptiScaler game name matching with manifest overrides
    - Use `optiScalerWikiNames` manifest dictionary to map game names to wiki names
    - Fall back to direct game name lookup when no override exists
    - Case-insensitive matching
    - _Requirements: 7.2_

  - [x] 3.4 Write property test for OptiScaler name matching (Property 6)
    - **Property 6: OptiScaler game name matching with overrides**
    - **Validates: Requirements 7.2**

  - [x] 3.5 Write property test for dual-list merge (Property 9)
    - **Property 9: Dual-list OptiScaler merge and display**
    - **Validates: Requirements 13.3, 15.2, 15.3**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Extend manifest model serialization and add round-trip test
  - [x] 5.1 Verify RemoteManifest JSON serialization/deserialization with new addon info properties
    - Ensure new nullable dictionary properties serialize to/from JSON correctly
    - Ensure existing manifest fields are unaffected
    - _Requirements: 6.1–6.4_

  - [x] 5.2 Write property test for manifest round-trip (Property 5)
    - **Property 5: Manifest round-trip preserves addon info entries**
    - **Validates: Requirements 6.1, 6.2, 6.4**

- [x] 6. Add Info buttons to Detail View (MainWindow.xaml + DetailPanelBuilder.Components.cs)
  - [x] 6.1 Modify MainWindow.xaml component row Grids to add Info button column
    - Insert new column (width 36) at position 2 in each component row Grid (REF, RS, RDX, Luma, UL, DC, OS)
    - Shift existing install button to col 3, copy/UE to col 4, delete to col 5
    - Add Info button elements: DetailRefInfoBtn, DetailRsInfoBtn, DetailRdxInfoBtn, DetailLumaInfoBtn, DetailUlInfoBtn, DetailDcInfoBtn, DetailOsInfoBtn
    - Style: font size 11, padding (6,2,6,2), SurfaceOverlayBrush background, TextSecondaryBrush foreground, BorderStrongBrush border, corner radius 8, width 36, height 32
    - _Requirements: 1.1–1.5_

  - [x] 6.2 Update DetailPanelBuilder.Components.cs to populate Info button state
    - Set Tag to card for each Info button
    - Call AddonInfoResolver.GetSourceType() to determine visual style (highlighted vs muted)
    - Apply AccentBlueBgBrush/AccentBlueBrush/AccentBlueBorderBrush for manifest/wiki content
    - Apply default muted style for fallback content
    - Set tooltip via AddonInfoResolver.GetTooltip()
    - Wire click handler to DialogService.ShowAddonInfoDialogAsync()
    - _Requirements: 1.1–1.5, 10.1–10.3, 11.1–11.5, 12.1–12.4_

  - [x] 6.3 Ensure Info button visibility syncs with component row visibility in PropertyChanged handler
    - Info button remains clickable at full opacity even when row is greyed out (opacity 0.35)
    - Info button visibility matches row visibility
    - _Requirements: 10.1–10.2_

- [x] 7. Add Info buttons to Card Flyout View (CardBuilder.Flyout.cs)
  - [x] 7.1 Modify BuildComponentRow to add Info button column
    - Add new `addonType` parameter to BuildComponentRow
    - Insert Info button column (width 36) between status text (col 1) and install button (col 2)
    - Shift install button to col 3, copy config to col 4, uninstall to col 5
    - Style: font size 11, padding (4,2,4,2), same color scheme as Detail view
    - _Requirements: 2.1–2.3_

  - [x] 7.2 Update all BuildComponentRow call sites in BuildInstallFlyoutContent
    - Pass appropriate AddonType for each component row
    - Wire Info button click handler to DialogService.ShowAddonInfoDialogAsync()
    - Apply highlighted/muted styling based on AddonInfoResolver.GetSourceType()
    - Set tooltip text
    - _Requirements: 2.1–2.3, 11.4, 12.1–12.4_

  - [x] 7.3 Update UpdateComponentRow to handle Info button styling refresh
    - Preserve Info button state during PropertyChanged update cycle
    - Update Info button visual style when content availability changes
    - _Requirements: 10.3, 11.5_

- [x] 8. Implement Info Content Dialog in DialogService.Game.cs
  - [x] 8.1 Create ShowAddonInfoDialogAsync method in DialogService.Game.cs
    - Accept GameCardViewModel and AddonType parameters
    - Call AddonInfoResolver.Resolve() to get content
    - Build ContentDialog with title "{AddonName} — {GameName}"
    - Use SurfaceToolbarBrush background, ElementTheme.Dark, "Close" button
    - Wrap content in ScrollViewer with MaxHeight 440
    - Render text with TextSecondaryBrush foreground, font size 13, line height 22
    - Render URLs as clickable hyperlinks with AccentBlueBrush
    - _Requirements: 5.1–5.5_

  - [x] 8.2 Handle RenoDX-specific dialog content
    - Display wiki status badge (reuse existing badge rendering from NotesButton_Click)
    - Show wiki notes and manifest gameNotes content
    - Include wiki page link (NameUrl) as clickable hyperlink
    - _Requirements: 8.3–8.5_

  - [x] 8.3 Handle Luma-specific dialog content
    - Display Luma wiki data (LumaMod notes) and lumaGameNotes manifest content
    - Reuse existing Luma notes rendering logic from NotesButton_Click
    - _Requirements: 8.6_

  - [x] 8.4 Handle OptiScaler-specific dialog content
    - Display compatibility status, supported upscalers, and notes
    - Show separate "OptiScaler Compatibility" and "FSR4 Compatibility" sections when both exist
    - Render detail page URLs as clickable hyperlinks
    - _Requirements: 7.3, 7.4, 15.2, 15.3_

  - [x] 8.5 Write property test for result field completeness (Property 7)
    - **Property 7: Info result includes all available fields**
    - **Validates: Requirements 7.3, 7.4, 8.5**

- [x] 9. Remove header-level Notes and Discussion buttons
  - [x] 9.1 Remove DetailNotesBtn and DetailDiscussionBtn from MainWindow.xaml
    - Remove the "ℹ" button element and "💬" button element from the header action buttons area
    - _Requirements: 8.1, 8.2_

  - [x] 9.2 Remove NotesButton_Click handler and related wiring from DialogService.Game.cs
    - Remove the old NotesButton_Click method (functionality migrated to ShowAddonInfoDialogAsync)
    - Remove DetailNotesBtn/DetailDiscussionBtn Tag assignments from DetailPanelBuilder.cs PopulateDetailPanel
    - Remove visibility bindings for DetailNotesBtn and DetailDiscussionBtn
    - _Requirements: 8.1, 8.2_

- [x] 10. Compact View integration
  - [x] 10.1 Verify Info buttons work in Compact View
    - Since CompactViewBuilder reuses DetailPanelBuilder, Info buttons should appear automatically
    - Verify click handlers work correctly in compact mode
    - Verify Info button styling updates on page navigation
    - _Requirements: 3.1, 3.2_

- [x] 11. Implement greyed-out state for Info buttons with no content
  - [x] 11.1 Add logic to grey out Info buttons when no content is available
    - When InfoSourceType is None: set opacity 0.3 and IsHitTestVisible false
    - When any content exists (including fallback): keep full opacity and clickable
    - Apply in both Detail view and Card flyout view
    - _Requirements: 14.1–14.3_

- [x] 12. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Wire Info button visual style updates on game selection change
  - [x] 13.1 Update Info button styling when game selection changes in Detail view
    - Re-evaluate AddonInfoResolver.GetSourceType() for each addon when a new game is selected
    - Apply highlighted (AccentBlue) or muted (SurfaceOverlay) styling accordingly
    - Update tooltips to reflect new game's content availability
    - _Requirements: 11.3, 11.5_

  - [x] 13.2 Update Info button styling in Card flyout when flyout opens for a different game
    - Evaluate content availability at flyout build time
    - Apply correct styling per addon
    - _Requirements: 11.4_

- [x] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The Compact View (Requirement 3) gets Info buttons automatically because it reuses the Detail panel — task 10 is a verification step
- The existing `NotesButton_Click` logic in DialogService.Game.cs serves as the template for the new `ShowAddonInfoDialogAsync` method
