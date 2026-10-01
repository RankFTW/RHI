# RHI for Linux / Proton (preview)

Native ReShade and RenoDX management for Proton games, tested on Bazzite x86_64.

## Install and run

1. Download **RHI-linux-x64.tar.gz** from this release (the source-code archives require building).
2. Extract the entire archive into a permanent folder, such as `~/Applications/RHI`.
3. Run `./run-linux.sh` inside that folder. No .NET installation or build is needed.
4. Optionally run `./install.sh` to add **RHI (Linux / Proton)** to the application menu. It asks **Add an RHI icon to your desktop? [y/N]**; answer **Y** for a desktop shortcut or **N** to skip it. Keep the extracted folder in place. Use `--desktop` or `--no-desktop` to choose without a prompt.

The package includes .NET and a static 7-Zip extractor; the Bash menu installer needs no Python. Bazzite supplies the native desktop libraries. Other distributions need X11/XWayland, fontconfig and the normal .NET native libraries; see the distro table in `LINUX.md` in the archive. Extractor redistribution notices and its source-code link are under `licenses/7zip/`. The app needs internet access to download plugins. Linux ARM and native Linux Vulkan games are not supported by this package.

Select a game, choose **Install recommended**, and follow **Finish Steam setup** if shown. See `LINUX.md` for setup, backups, and the features supported by the Linux port. Windows-only driver management and full Windows feature parity are not included.

## Update

Close RHI, extract the new release into a new permanent folder, and run its `./install.sh` to update the menu shortcut. Preferences remain in your user data directory; managed plugins and their backups remain beside each game. You can then remove the previous application folder.

## Verify the download

Download the `.sha256` file beside the archive and run:

```bash
sha256sum -c RHI-linux-x64.tar.gz.sha256
```

`BUILD-INFO.txt` records the source commit used for this package.
