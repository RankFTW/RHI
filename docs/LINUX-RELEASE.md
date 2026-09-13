# RHI for Linux / Proton (preview)

Native ReShade and RenoDX management for Proton games, tested on Bazzite x86_64.

## Install and run

1. Download **RHI-linux-x64.tar.gz** from this release (the source-code archives require building).
2. Extract the entire archive into a permanent folder, such as `~/Applications/RHI`.
3. Run `./run-linux.sh` inside that folder. No .NET installation or build is needed.
4. Optionally run `./install.sh` to add **RHI (Linux / Proton)** to the application menu. Keep the extracted folder in place.

Bazzite includes the desktop libraries, Python 3 (for the menu installer), and `7z` (for ReShade extraction). Other distributions need these dependencies; see `LINUX.md` in the archive. The app needs internet access to download plugins. Linux ARM and native Linux Vulkan games are not supported by this package.

Select a game, choose **Install recommended**, and follow **Finish Steam setup** if shown. See `LINUX.md` for setup, backups, and the features supported by the Linux port. Windows-only driver management and full Windows feature parity are not included.

## Update

Close RHI, extract the new release into a new permanent folder, and run its `./install.sh` to update the menu shortcut. Preferences remain in your user data directory; managed plugins and their backups remain beside each game. You can then remove the previous application folder.

## Verify the download

Download the `.sha256` file beside the archive and run:

```bash
sha256sum -c RHI-linux-x64.tar.gz.sha256
```

`BUILD-INFO.txt` records the source commit used for this package.
