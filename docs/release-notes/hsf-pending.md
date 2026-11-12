# HSF patch notes (held back)

HSF is hidden behind the manifest flag `featureFlags.hsf` until Capt-Hunter makes the HSF repo public. When the flag goes on, add these to the current version's notes (`RenoDXCommander/RHI_PatchNotes.md`, and the beta notes file during betas).

## New

**HSF frame generation**
- New HSF row in Extras › MFG Unlocks. HSF (Hunter's Sloppy Frames, by Capt-Hunter) is a ReShade add-on that adds DLSS frame generation, up to 6x, to DirectX 9, 11 and 12 games that don't have it, 32-bit games included.
- One click installs everything it needs: the add-on, HSF's Streamline and frame generation files, HSF.ini, HSF.fx and iMMERSE LaunchPad for optical flow. Set the multiplier and turn FRAME GEN on in HSF's tab in the ReShade overlay.
- DirectX 12 games work from the first launch: RHI sets up HSF's DirectX 12 mode before the game starts, so there's no restart.
- RHI keeps HSF up to date in the games it installed it in, and keeps your HSF settings. The ⚙ shows the installed version and DirectX 12 mode, and turns LaunchPad off if you use LumeniteFX or the game's own motion instead.
- RTX 20, 30 and 40 cards: installing HSF offers Universal MFG Unlocker too, which those cards need for HSF's frame generation (RTX 40 for more than 2x). RHI detects your graphics card and doesn't ask on RTX 50. DirectX 12 games get the usual DLL name choice; DirectX 9, 11 and 32-bit games get it in HSF's own folder, where HSF loads it. It comes without the RTXMFG-XeSS folder, which HSF doesn't use.
- Needs ReShade with add-on support and an RTX 40 or newer GPU (RTX 20 and 30 through Universal MFG Unlocker). Never use it in games with online anti-cheat.

## Bug Fixes

- Fixed games with HSF installed showing HSF's own DLSS frame generation and Streamline files as the game's, which let DLSS updates replace them.
