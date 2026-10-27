# Requirements Document

## Introduction

Add a new "Bitness & API Override" section to the per-game overrides panel in RenoDXCommander. This section allows users to manually override the auto-detected bitness (32-bit or 64-bit) and the detected graphics APIs for a game. Auto-detection continues to run on each app load, but the user's manual override takes priority when set. The new section is placed between the existing Middle Row (shaders / global update toggles) and the Bottom Row (DLL naming override) in the `DetailPanelBuilder.BuildOverridesPanel` layout.

The bitness override addresses cases where PE header analysis misidentifies a game's architecture. The API override addresses cases where import-table scanning returns incorrect or incomplete results — particularly useful for dual-API games (e.g. DX11 + Vulkan) where the user wants to suppress one API so only the relevant mod options appear.

## Glossary

- **Overrides_Panel**: The dynamically-built `StackPanel` (named `OverridesPanel`) inside the detail view that contains per-game override controls for the currently selected game.
- **Bitness_API_Section**: The new row inserted into the Overrides_Panel between the Middle Row (shaders/global updates) and the Bottom Row (DLL naming override), containing the Bitness_Override and API_Override controls.
- **Bitness_Override**: A control that lets the user override the auto-detected architecture of a game's executable. Values: "Auto" (use detection), "32-bit", or "64-bit".
- **API_Override**: A set of toggle controls, one per graphics API value, that let the user manually flag which APIs a game uses. Each toggle can be turned on or off independently.
- **Auto_Detect**: The existing PE header analysis (`PeHeaderService`) and import-table scanning (`GraphicsApiDetector`) that determines bitness and graphics APIs on each app load.
- **GameCardViewModel**: The ViewModel representing a single game card, exposing `Is32Bit`, `GraphicsApi`, `DetectedApis`, and `IsDualApiGame` properties.
- **GraphicsApiType**: The enum defining supported API values: Unknown, DirectX8, DirectX9, DirectX10, DirectX11, DirectX12, Vulkan, OpenGL.
- **Effective_Bitness**: The resolved bitness for a game after applying the user's override (if any) on top of auto-detection.
- **Effective_APIs**: The resolved set of graphics APIs for a game after applying the user's API toggle overrides on top of auto-detection.
- **DLL_Naming_Section**: The existing bottom row containing the DLL override toggle and ReShade filename ComboBox.
- **Middle_Row**: The existing row containing the Shaders section (left) and Global update inclusion toggles (right).

## Requirements

### Requirement 1: Bitness & API Section Placement

**User Story:** As a user, I want the bitness and API override controls in their own section between the shader/update toggles and the DLL naming override, so that detection overrides are logically grouped and easy to find.

#### Acceptance Criteria

1. WHEN the Overrides_Panel is built, THE Bitness_API_Section SHALL appear immediately after the Middle_Row separator and before the DLL_Naming_Section.
2. THE Bitness_API_Section SHALL be separated from the Middle_Row above and the DLL_Naming_Section below by `UIFactory.MakeSeparator()` horizontal separators.
3. THE Bitness_API_Section SHALL render as a two-section horizontal layout with a vertical divider separating the Bitness_Override (left) from the API_Override (right), using the same divider style as other override rows (1px wide, `BorderDefaultBrush`, 12px horizontal margin).

### Requirement 2: Bitness Override

**User Story:** As a user, I want to manually set a game's bitness to 32-bit or 64-bit when auto-detection is incorrect, so that the correct default DLL names are used without waiting for a manifest update.

#### Acceptance Criteria

1. THE Bitness_Override SHALL display a section label "Bitness" above the control.
2. THE Bitness_Override SHALL provide three selectable options: "Auto", "32-bit", and "64-bit".
3. WHEN "Auto" is selected, THE Overrides_Panel SHALL use the auto-detected bitness from `PeHeaderService` PE header analysis as the Effective_Bitness.
4. WHEN "32-bit" is selected, THE Overrides_Panel SHALL set the Effective_Bitness to 32-bit regardless of auto-detection.
5. WHEN "64-bit" is selected, THE Overrides_Panel SHALL set the Effective_Bitness to 64-bit regardless of auto-detection.
6. WHEN the Effective_Bitness changes, THE GameCardViewModel `Is32Bit` property SHALL update to reflect the new value.
7. WHEN the Effective_Bitness changes, THE DLL_Naming_Section default ReShade filename (placeholder text) SHALL update to match the new bitness ("ReShade32.dll" for 32-bit, "ReShade64.dll" for 64-bit).
8. THE Bitness_Override SHALL default to "Auto" when no user override exists for the game.

### Requirement 3: API Override Toggles

**User Story:** As a user, I want to toggle individual graphics APIs on or off for a game, so that I can correct misdetected APIs or suppress one API on a dual-API game to see only the relevant mod options.

#### Acceptance Criteria

1. THE API_Override SHALL display a section label "Graphics API" above the toggle controls.
2. THE API_Override SHALL display one toggle for each non-Unknown value in the GraphicsApiType enum: DirectX8, DirectX9, DirectX10, DirectX11, DirectX12, Vulkan, OpenGL.
3. WHEN no user API override exists for a game, THE API_Override toggles SHALL reflect the auto-detected state: toggles for APIs present in `DetectedApis` are ON, all others are OFF.
4. WHEN the user turns a toggle ON, THE Effective_APIs SHALL include that API regardless of auto-detection.
5. WHEN the user turns a toggle OFF, THE Effective_APIs SHALL exclude that API regardless of auto-detection.
6. WHEN the Effective_APIs change, THE GameCardViewModel `DetectedApis` property SHALL update to reflect the new set.
7. WHEN the Effective_APIs change, THE GameCardViewModel `IsDualApiGame` property SHALL be recomputed based on the new Effective_APIs.
8. WHEN the Effective_APIs change, THE GameCardViewModel `GraphicsApi` property (primary badge) SHALL be recomputed from the new Effective_APIs.

### Requirement 4: Persistence

**User Story:** As a user, I want my bitness and API overrides to persist across app restarts, so that I do not have to re-enter them each time.

#### Acceptance Criteria

1. WHEN the user changes the Bitness_Override selection, THE Overrides_Panel SHALL persist the override immediately using the same per-game dictionary pattern as existing overrides (e.g. `_perGameShaderMode`, `_vulkanRenderingPaths`).
2. WHEN the user changes any API_Override toggle, THE Overrides_Panel SHALL persist the full set of API override states immediately using the same per-game dictionary pattern.
3. WHEN the application loads a game, THE MainViewModel SHALL apply persisted bitness overrides before setting `Is32Bit` on the GameCardViewModel.
4. WHEN the application loads a game, THE MainViewModel SHALL apply persisted API overrides before setting `DetectedApis`, `IsDualApiGame`, and `GraphicsApi` on the GameCardViewModel.
5. WHEN no persisted override exists for a game, THE application SHALL fall back to auto-detection for both bitness and APIs.

### Requirement 5: Reset Integration

**User Story:** As a user, I want the existing "Reset Overrides" button to also clear my bitness and API overrides, so that I can return to fully auto-detected values with a single action.

#### Acceptance Criteria

1. WHEN the user clicks the Reset Overrides button, THE Bitness_Override SHALL revert to "Auto".
2. WHEN the user clicks the Reset Overrides button, THE API_Override toggles SHALL revert to reflecting the auto-detected state.
3. WHEN the user clicks the Reset Overrides button, THE persisted bitness and API overrides for the game SHALL be removed.
4. WHEN the user clicks the Reset Overrides button, THE GameCardViewModel `Is32Bit`, `DetectedApis`, `IsDualApiGame`, and `GraphicsApi` properties SHALL revert to their auto-detected values.

### Requirement 6: Downstream Effects

**User Story:** As a user, I want changes to bitness and API overrides to immediately update all dependent UI elements and behaviors, so that the app reflects my overrides consistently.

#### Acceptance Criteria

1. WHEN the Effective_Bitness changes, THE detail panel 32-bit badge SHALL update its visibility to match the new `Is32Bit` value.
2. WHEN the Effective_APIs change such that `IsDualApiGame` becomes true, THE Rendering Path ComboBox SHALL appear in the Top Row of the Overrides_Panel.
3. WHEN the Effective_APIs change such that `IsDualApiGame` becomes false, THE Rendering Path ComboBox SHALL be removed from the Top Row of the Overrides_Panel.
4. WHEN the Effective_APIs change, THE detail panel Graphics API badge SHALL update its text to reflect the new primary `GraphicsApi` value.
5. WHEN the Effective_APIs change, THE install behavior (DX DLL injection vs Vulkan layer) SHALL use the updated API information.

### Requirement 7: Visual Consistency

**User Story:** As a user, I want the new override section to match the visual style of the existing overrides panel, so that the UI feels cohesive.

#### Acceptance Criteria

1. THE Bitness_API_Section SHALL use the same font sizes, brush resources, corner radii, and padding values as the existing override sections.
2. THE API_Override toggles SHALL use the same `ToggleSwitch` styling (FontSize, Foreground, OnContent, OffContent) as the existing Global update inclusion toggles.
3. THE Bitness_Override control SHALL use styling consistent with other selection controls in the overrides panel (ComboBox or RadioButtons with matching font size and brush resources).
