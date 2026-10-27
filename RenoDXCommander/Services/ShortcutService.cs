using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Creates desktop shortcuts pointing to RHI.exe --launch "GameName".
///
/// Icon strategy (matching how Steam/Playnite/GOG Galaxy do it):
///   Steam games  → download header image from Steam CDN, convert to .ico via raw DIB
///   Xbox games   → download MS Store tile image, convert to .ico via raw DIB
///   Other games  → point .lnk icon directly at the game exe (Windows renders natively)
///   Fallback     → point .lnk icon at RHI.exe itself
/// </summary>
public static class ShortcutService
{
    // ── IShellLink COM ────────────────────────────────────────────────────────

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    // ── GDI for image → ICO conversion (Steam CDN images) ────────────────────

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hDC, ref BITMAPINFO bmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(IntPtr hDC,
        int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight,
        byte[] lpBits, ref BITMAPINFO lpBMI, uint iUsage, uint dwRop);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint   biSize, biSizeImage, biClrUsed, biClrImportant;
        public int    biWidth, biHeight, biXPelsPerMeter, biYPelsPerMeter;
        public ushort biPlanes, biBitCount;
        public uint   biCompression;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] bmiColors;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static async Task<string?> CreateGameShortcutAsync(
        string gameName,
        GameCardViewModel card,
        IGameNameService gameNameService,
        RemoteManifest? manifest,
        System.Net.Http.HttpClient http,
        IPcgwService? pcgwService = null)
    {
        try
        {
            var exeDir     = AppPaths.ExeDir;
            var rhiExePath = Path.Combine(exeDir, "RHI.exe");
            var desktop    = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var safeName   = string.Concat(gameName.Split(Path.GetInvalidFileNameChars()));
            var lnkPath    = Path.Combine(desktop, $"{safeName}.lnk");

            var iconDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "ShortcutIcons");
            Directory.CreateDirectory(iconDir);

            string? iconPath = null;
            int     iconIndex = 0;

            var steamAppId = card.DetectedGame?.SteamAppId;
            var xboxAumid  = card.DetectedGame?.XboxAumid;

            if (steamAppId > 0)
            {
                // Steam: use exe icon directly — same as GOG/EA/Epic, always accessible
                var exePath = await ResolveGameExePathAsync(card, gameNameService, manifest);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                { iconPath = exePath; iconIndex = 0; }
            }
            else if (!string.IsNullOrEmpty(xboxAumid))
            {
                // Xbox/Game Pass: try PCGW central data for a Steam AppID first,
                // then fall back to MS Store box art via package family name
                var icoPath = Path.Combine(iconDir, $"{safeName}.ico");
                bool downloaded = false;

                try
                {
                    var pcgwSteamId = pcgwService?.GetSteamAppIdFromCentralData(gameName);
                    if (pcgwSteamId.HasValue && pcgwSteamId.Value > 0)
                    {
                        CrashReporter.Log($"[ShortcutService] Xbox '{gameName}' — PCGW Steam AppID={pcgwSteamId}");
                        downloaded = await TryDownloadSteamIconAsync(http, pcgwSteamId.Value, icoPath);
                    }

                    if (!downloaded)
                        downloaded = await TryDownloadMsStoreIconAsync(http, gameName, xboxAumid, icoPath);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[ShortcutService] Xbox icon fetch failed for '{gameName}' — {ex.Message}. Using RHI icon.");
                }

                if (downloaded)
                    iconPath = icoPath;
            }
            else
            {
                // GOG / EA / Epic / manual: point directly at the game exe
                // Windows renders the icon natively at full quality
                var exePath = await ResolveGameExePathAsync(card, gameNameService, manifest);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    iconPath  = exePath;
                    iconIndex = 0;
                }
            }

            // Fallback: use RHI's own icon
            if (string.IsNullOrEmpty(iconPath))
                iconPath = rhiExePath;

            CrashReporter.Log($"[ShortcutService] '{gameName}' icon={iconPath}");

            CreateShellLink(
                targetPath:  rhiExePath,
                arguments:   $"--launch \"{gameName}\"",
                workingDir:  exeDir,
                iconPath:    iconPath,
                iconIndex:   iconIndex,
                description: $"Launch {gameName} via RHI",
                outputPath:  lnkPath);

            return lnkPath;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShortcutService.CreateGameShortcutAsync] Failed for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    // ── MS Store icon download (Xbox/Game Pass) ───────────────────────────────

    /// <summary>
    /// Searches the MS Store by game name + AUMID package family name, fetches the
    /// BoxArt image (square, ~2160×2160) and converts it to a 256×256 ICO.
    /// No authentication required — uses the public storeedgefd catalog API.
    /// </summary>
    private static async Task<bool> TryDownloadMsStoreIconAsync(
        System.Net.Http.HttpClient http, string gameName, string aumid, string outputIcoPath)
    {
        try
        {
            // Extract package family name from AUMID: "Microsoft.Foo_8wekyb3d8bbwe!App" → "Microsoft.Foo_8wekyb3d8bbwe"
            var pfn = aumid.Contains('!') ? aumid.Substring(0, aumid.IndexOf('!')) : aumid;

            // Direct product lookup by package family name — no search needed
            var url = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{Uri.EscapeDataString(pfn)}?market=US&locale=en-US&deviceFamily=Windows.Desktop&idType=PackageFamilyName";
            CrashReporter.Log($"[ShortcutService] MS Store PFN lookup: {pfn}");

            var json = await http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var images = doc.RootElement.GetProperty("Payload").GetProperty("Images");

            // Prefer BoxArt (square) → Poster → hero
            string? imageUrl = null;
            foreach (var priority in new[] { "BoxArt", "Poster", "hero" })
            {
                foreach (var img in images.EnumerateArray())
                {
                    if (img.TryGetProperty("ImageType", out var typeEl)
                        && typeEl.GetString()?.Equals(priority, StringComparison.OrdinalIgnoreCase) == true
                        && img.TryGetProperty("Url", out var urlEl))
                    {
                        imageUrl = urlEl.GetString();
                        if (!string.IsNullOrEmpty(imageUrl)) break;
                    }
                }
                if (!string.IsNullOrEmpty(imageUrl)) break;
            }

            if (string.IsNullOrEmpty(imageUrl))
            {
                CrashReporter.Log($"[ShortcutService] MS Store: no image found for '{gameName}' (pfn={pfn})");
                return false;
            }

            CrashReporter.Log($"[ShortcutService] MS Store: downloading {imageUrl}");
            var bytes = await http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
            if (bytes.Length < 100) return false;

            ConvertJpegToIco(bytes, 256, outputIcoPath);
            CrashReporter.Log($"[ShortcutService] MS Store icon saved: {outputIcoPath}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShortcutService.TryDownloadMsStoreIconAsync] '{gameName}': {ex.Message}");
            return false;
        }
    }

    // ── Steam CDN icon download ───────────────────────────────────────────────

    /// <summary>
    /// Downloads the Steam library cover art (600×900) or header (460×215) for
    /// the given appId and saves it as a 256×256 .ico file.
    /// Returns true on success.
    /// </summary>
    private static async Task<bool> TryDownloadSteamIconAsync(
        System.Net.Http.HttpClient http, int appId, string outputIcoPath)
    {
        // Download the image and save it. Try CDN paths first, then Steam Store API.
        // We save as .jpg — Windows supports JPEG as shortcut icon sources directly,
        // avoiding the need for WIC JPEG decoding or any ICO conversion.
        byte[]? imageBytes = null;

        var cdnUrls = new[]
        {
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
        };

        foreach (var url in cdnUrls)
        {
            try
            {
                CrashReporter.Log($"[ShortcutService] Steam CDN: {url}");
                var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (bytes.Length > 100) { imageBytes = bytes; break; }
            }
            catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { /* 404 is expected for missing CDN assets; try the next URL */ }
            catch (Exception ex) { CrashReporter.Log($"[ShortcutService.TryDownloadSteamIconAsync] Steam CDN download failed — {ex.Message}"); }
        }

        // CDN miss — try Steam Store API for the hashed URL
        if (imageBytes == null)
        {
            try
            {
                CrashReporter.Log($"[ShortcutService] Steam CDN miss for {appId}, trying Store API");
                var apiUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic";
                var json   = await http.GetStringAsync(apiUrl).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement.GetProperty(appId.ToString());
                if (root.GetProperty("success").GetBoolean())
                {
                    var data = root.GetProperty("data");
                    foreach (var field in new[] { "header_image", "capsule_image" })
                    {
                        if (data.TryGetProperty(field, out var imgEl))
                        {
                            var imgUrl = imgEl.GetString();
                            if (string.IsNullOrEmpty(imgUrl)) continue;
                            try
                            {
                                CrashReporter.Log($"[ShortcutService] Steam Store API image: {imgUrl}");
                                var bytes = await http.GetByteArrayAsync(imgUrl).ConfigureAwait(false);
                                if (bytes.Length > 100) { imageBytes = bytes; break; }
                            }
                            catch (Exception ex) { CrashReporter.Log($"[ShortcutService.TryDownloadSteamIconAsync] Steam Store API image download failed — {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[ShortcutService.TryDownloadSteamIconAsync] Store API failed appId={appId}: {ex.Message}");
            }
        }

        if (imageBytes == null) return false;

        // Convert JPEG to ICO via WIC (CoCreateInstance path)
        ConvertJpegToIco(imageBytes, 256, outputIcoPath);
        CrashReporter.Log($"[ShortcutService] Steam icon saved: {outputIcoPath}");
        return true;
    }

    // ── JPEG → ICO via WIC (Windows Imaging Component) ───────────────────────

    // ── JPEG → ICO via Windows.Graphics.Imaging (WinRT) ─────────────────────

    /// <summary>
    /// Decodes a JPEG byte array using Windows.Graphics.Imaging (WinRT),
    /// <summary>
    /// Decodes a JPEG byte array using Windows.Graphics.Imaging (WinRT),
    /// scales it to <paramref name="size"/>×<paramref name="size"/> and writes a raw 32bpp ICO file.
    /// Uses the WinRT API already available in this WinUI 3 process — no WIC P/Invoke needed.
    /// </summary>
    private static void ConvertJpegToIco(byte[] jpegBytes, int size, string outputPath)
    {
        // WinRT async APIs must run on an STA thread
        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                // Write JPEG to an in-memory stream
                var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var writer = new Windows.Storage.Streams.DataWriter(ras);
                writer.WriteBytes(jpegBytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                ras.Seek(0);

                // Decode JPEG
                var decoder = Windows.Graphics.Imaging.BitmapDecoder
                    .CreateAsync(ras).AsTask().GetAwaiter().GetResult();

                // Get pixels scaled to size×size as BGRA8
                var transform = new Windows.Graphics.Imaging.BitmapTransform
                {
                    ScaledWidth  = (uint)size,
                    ScaledHeight = (uint)size,
                    InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
                };
                var pixelData = decoder.GetPixelDataAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                    transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)
                    .AsTask().GetAwaiter().GetResult();

                var pixels = pixelData.DetachPixelData();
                WriteRawIco(pixels, size, outputPath);
            }
            catch (Exception ex) { threadEx = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (threadEx != null) throw threadEx;
    }

    // ── Raw ICO writer (32bpp uncompressed DIB) ───────────────────────────────

    private static void WriteRawIco(byte[] bgraTopDown, int size, string path)
    {
        int stride     = size * 4;
        int pixelBytes = size * stride;

        // ICO DIB pixels must be bottom-up
        var bgraBottomUp = new byte[pixelBytes];
        for (int row = 0; row < size; row++)
            Array.Copy(bgraTopDown, row * stride, bgraBottomUp, (size - 1 - row) * stride, stride);

        // Monochrome AND mask (all zeros = use alpha channel)
        int maskStride = ((size + 31) / 32) * 4;
        var andMask    = new byte[size * maskStride];
        int dibSize    = 40 + pixelBytes + andMask.Length; // 40 = sizeof(BITMAPINFOHEADER)

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICO header
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)1);

        // Directory entry
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write((uint)dibSize);
        bw.Write((uint)(6 + 16)); // data offset

        // BITMAPINFOHEADER
        bw.Write((uint)40);
        bw.Write((int)size);
        bw.Write((int)(size * 2)); // height × 2 for ICO
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)0);   // BI_RGB
        bw.Write((uint)pixelBytes);
        bw.Write((int)0); bw.Write((int)0);
        bw.Write((uint)0); bw.Write((uint)0);

        bw.Write(bgraBottomUp);
        bw.Write(andMask);
    }

    // ── IShellLink writer ─────────────────────────────────────────────────────

    private static void CreateShellLink(string targetPath, string arguments,
        string workingDir, string iconPath, int iconIndex, string description, string outputPath)
    {
        var shellLink = (IShellLinkW)new ShellLink();
        shellLink.SetPath(targetPath);
        shellLink.SetArguments(arguments);
        shellLink.SetWorkingDirectory(workingDir);
        shellLink.SetIconLocation(iconPath, iconIndex);
        shellLink.SetDescription(description);
        shellLink.SetShowCmd(1);
        ((IPersistFile)shellLink).Save(outputPath, false);
    }

    // ── Game exe resolution ───────────────────────────────────────────────────

    public static async Task<string?> ResolveGameExePathAsync(
        GameCardViewModel card,
        IGameNameService gameNameService,
        RemoteManifest? manifest)
    {
        var gameName = card.GameName;

        if (gameNameService.LaunchExeOverrides.TryGetValue(gameName, out var userExe)
            && !string.IsNullOrEmpty(userExe) && File.Exists(userExe))
            return userExe;

        if (manifest?.LaunchExeOverrides?.TryGetValue(gameName, out var manifestExe) == true
            && !string.IsNullOrEmpty(manifestExe) && !string.IsNullOrEmpty(card.InstallPath))
        {
            var full = Path.Combine(card.InstallPath, manifestExe);
            if (File.Exists(full)) return full;
        }

        if (!string.IsNullOrEmpty(card.DetectedGame?.EpicAppName)) return null;

        if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
        {
            var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "CrashReportClient", "launcher" };
            var installPath = card.InstallPath;
            return await Task.Run(() =>
            {
                try
                {
                    return Directory.EnumerateFiles(installPath, "*.exe",
                            string.IsNullOrEmpty(card.DetectedGame?.XboxAumid)
                                ? SearchOption.TopDirectoryOnly
                                : SearchOption.AllDirectories)
                        .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                        .OrderByDescending(e => { try { return new FileInfo(e).Length; } catch { return 0L; } })
                        .FirstOrDefault();
                }
                catch { return null; }
            });
        }

        return null;
    }

    // ── COM IStream wrapper ───────────────────────────────────────────────────

    private class ManagedIStream : System.Runtime.InteropServices.ComTypes.IStream
    {
        private readonly Stream _s;
        public ManagedIStream(Stream s) => _s = s;
        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int n = _s.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
        }
        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => _s.Write(pv, 0, cb);
        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long pos = _s.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
        }
        public void SetSize(long libNewSize) { }
        public void CopyTo(System.Runtime.InteropServices.ComTypes.IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) { }
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) { }
        public void UnlockRegion(long libOffset, long cb, int dwLockType) { }
        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG { cbSize = _s.Length };
        }
        public void Clone(out System.Runtime.InteropServices.ComTypes.IStream ppstm) => throw new NotImplementedException();
    }
}
