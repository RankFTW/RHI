# Requirements Document

## Introduction

When a user provides a ReShade preset `.ini` file to RHI (via drag-and-drop or the existing preset selection dialog), the application should validate the file as a genuine ReShade preset, store it for reuse, and optionally resolve and install the shader packs required by that preset. This eliminates the manual process of figuring out which shader packs a preset needs and configuring them per-game.

## Glossary

- **RHI**: RenoDX HDR Injector — the WinUI 3 desktop application (RenoDXCommander)
- **Preset_INI**: A ReShade preset `.ini` file containing technique and shader configuration
- **Techniques_Line**: The `Techniques=` entry in a Preset_INI, formatted as `TechniqueName@ShaderFile.fx,TechniqueName2@ShaderFile2.fx,...`
- **Shader_Pack**: A downloadable collection of ReShade shader (`.fx`) and texture files, identified by a unique pack ID in ShaderPackService
- **Presets_Folder**: The directory `%LocalAppData%\RHI\inis\reshade-presets\` where preset INI files are stored for reuse
- **Per_Game_Shader_Mode**: A per-game override that switches shader deployment from the global selection to a game-specific set of shader packs (the "Select" mode)
- **ShaderPack_FileList**: The JSON list stored as `ShaderPack_{id}_Files` in settings, containing all relative file paths belonging to a shader pack
- **DragDropHandler**: The service class that routes dropped files to the appropriate processing flow
- **PresetPopupHelper**: The helper class that shows the preset selection dialog and deploys selected presets to game folders
- **SyncGameFolder**: The ShaderPackService method that synchronises the game-local reshade-shaders folder to match the current shader pack selection

## Requirements

### Requirement 1: ReShade Preset INI Validation

**User Story:** As a user, I want RHI to distinguish ReShade preset INI files from arbitrary INI files, so that only valid presets are processed.

#### Acceptance Criteria

1. WHEN a `.ini` file is provided, THE Preset_Validator SHALL check for the presence of a `Techniques=` line containing at least one entry in the format `TechniqueName@ShaderFile.fx`
2. WHEN the `.ini` file contains a valid `Techniques=` line with one or more `@.fx` entries, THE Preset_Validator SHALL accept the file as a valid ReShade preset
3. WHEN the `.ini` file does not contain a `Techniques=` line, or the line contains no `@.fx` entries, THE Preset_Validator SHALL reject the file and display an error dialog stating the file is not a recognised ReShade preset
4. THE Preset_Validator SHALL perform case-insensitive matching on the `Techniques` key name

### Requirement 2: Preset Storage

**User Story:** As a user, I want drag-and-dropped presets to be saved to the presets folder automatically, so that I can reuse them across games without re-importing.

#### Acceptance Criteria

1. WHEN a valid Preset_INI is dropped onto the RHI window, THE DragDropHandler SHALL copy the file to the Presets_Folder before proceeding with game selection
2. IF the Presets_Folder does not exist, THEN THE DragDropHandler SHALL create it before copying
3. IF a file with the same name already exists in the Presets_Folder, THEN THE DragDropHandler SHALL overwrite the existing file
4. THE DragDropHandler SHALL preserve the original filename when copying to the Presets_Folder

### Requirement 3: Drag-and-Drop Entry Point

**User Story:** As a user, I want to drag and drop a `.ini` preset file onto the RHI window, so that I can quickly install a preset and its required shaders for a game.

#### Acceptance Criteria

1. WHEN a `.ini` file is dropped onto the RHI window, THE DragDropHandler SHALL validate the file as a ReShade preset using the Preset_Validator
2. WHEN the preset is valid, THE DragDropHandler SHALL copy the preset to the Presets_Folder
3. WHEN the preset is stored, THE DragDropHandler SHALL show a game selection dialog listing all detected games (following the same pattern as addon drag-and-drop)
4. WHEN the user selects a game and confirms, THE DragDropHandler SHALL copy the Preset_INI to the game's install folder
5. WHEN the preset is deployed to the game folder, THE DragDropHandler SHALL show a confirmation dialog asking "Also install the required shaders and textures?" with Yes and No options
6. WHEN the user selects Yes on the shader confirmation, THE DragDropHandler SHALL trigger the shader resolution and deployment flow for the selected game
7. WHEN the user selects No on the shader confirmation, THE DragDropHandler SHALL complete without modifying shader settings
8. IF the user cancels the game selection dialog, THEN THE DragDropHandler SHALL abort the flow without deploying the preset (the preset remains in the Presets_Folder)

### Requirement 4: Preset Selection Entry Point

**User Story:** As a user, I want to select a preset from the existing preset dialog and have RHI offer to install the required shaders, so that I get the same shader resolution from both entry points.

#### Acceptance Criteria

1. WHEN the user selects one or more presets from the PresetPopupHelper dialog and clicks Deploy, THE PresetPopupHelper SHALL deploy the selected presets to the current game's install folder
2. WHEN presets are deployed via the dialog, THE PresetPopupHelper SHALL show a confirmation dialog asking "Also install the required shaders and textures?" with Yes and No options
3. WHEN the user selects Yes, THE PresetPopupHelper SHALL trigger the shader resolution and deployment flow for the current game, using the union of techniques from all selected presets
4. WHEN the user selects No, THE PresetPopupHelper SHALL complete without modifying shader settings

### Requirement 5: Shader Resolution from Preset Techniques

**User Story:** As a user, I want RHI to automatically determine which shader packs contain the shaders referenced by my preset, so that I do not have to manually find and select them.

#### Acceptance Criteria

1. THE Shader_Resolver SHALL parse the `Techniques` line from a Preset_INI and extract all `.fx` filenames (the portion after the `@` symbol in each comma-separated entry)
2. THE Shader_Resolver SHALL look up each extracted `.fx` filename against all ShaderPack_FileLists to identify which Shader_Packs contain that file
3. THE Shader_Resolver SHALL select all Shader_Packs that contain at least one of the required `.fx` files
4. WHEN a `.fx` filename is not found in any ShaderPack_FileList, THE Shader_Resolver SHALL log a warning identifying the unresolved shader file
5. THE Shader_Resolver SHALL perform case-insensitive matching when comparing `.fx` filenames against ShaderPack_FileLists

### Requirement 6: Per-Game Shader Mode Configuration

**User Story:** As a user, I want RHI to automatically configure per-game shader selection for the target game when I install preset shaders, so that only the required packs are deployed to that game.

#### Acceptance Criteria

1. WHEN the shader resolution produces a non-empty set of Shader_Packs, THE RHI SHALL switch the target game to Per_Game_Shader_Mode "Select" with the resolved pack IDs
2. WHEN the game already has a Per_Game_Shader_Mode of "Select" with existing pack selections, THE RHI SHALL merge the resolved pack IDs with the existing selection (union of both sets)
3. WHEN the Per_Game_Shader_Mode is updated, THE RHI SHALL persist the selection via the existing PerGameShaderSelection mechanism
4. WHEN the Per_Game_Shader_Mode is updated, THE RHI SHALL call SyncGameFolder to deploy the resolved shader packs to the game's reshade-shaders folder

### Requirement 7: Techniques Line Parsing

**User Story:** As a user, I want the techniques parser to handle real-world preset files correctly, so that shader resolution works reliably.

#### Acceptance Criteria

1. THE Techniques_Parser SHALL split the `Techniques=` value on commas to produce individual technique entries
2. THE Techniques_Parser SHALL extract the `.fx` filename from each entry by taking the portion after the `@` character
3. WHEN a technique entry does not contain an `@` character, THE Techniques_Parser SHALL skip that entry without error
4. THE Techniques_Parser SHALL handle whitespace around commas and around the `@` separator
5. THE Techniques_Parser SHALL return a deduplicated set of `.fx` filenames
6. FOR ALL valid Techniques lines, parsing then reconstructing the `.fx` filename set SHALL produce the same set regardless of entry order (round-trip property on the extracted set)

### Requirement 8: Allowed Extensions Update

**User Story:** As a user, I want to be able to drop `.ini` files onto the RHI window, so that the drag-and-drop handler accepts them.

#### Acceptance Criteria

1. THE DragDropHandler SHALL include `.ini` in the AllowedExtensions set
2. WHEN a `.ini` file is dropped, THE DragDropHandler SHALL route the file to the preset processing flow
3. WHEN a `.ini` file is dropped that fails ReShade preset validation, THE DragDropHandler SHALL display an error dialog and not process the file further
