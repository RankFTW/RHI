namespace RenoDXCommander.Services;

// Shared discovery policy; callers decide whether OptiScaler copies are valid fallbacks.
public static class DlssFileDiscovery
{
    public const string Dlss = "nvngx_dlss.dll", Dlssd = "nvngx_dlssd.dll", Dlssg = "nvngx_dlssg.dll", Dlssnr = "nvngx_dlssnr.dll";
    public const string StreamlineIndicator = "sl.common.dll";
    public static readonly string[] DllNames = [Dlss, Dlssd, Dlssg, Dlssnr];
    public static readonly string[] StreamlineDlls =
    [
        "sl.common.dll", "sl.deepdvc.dll", "sl.directsr.dll", "sl.dlss.dll", "sl.dlss_d.dll",
        "sl.dlss_g.dll", "sl.interposer.dll", "sl.nis.dll", "sl.nvperf.dll", "sl.pcl.dll", "sl.reflex.dll",
    ];

    public sealed class Result
    {
        public Dictionary<string, string> GameDlls { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OptiScalerDlls { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? StreamlineFolder { get; internal set; }
        public string? StreamlineIndicatorPath { get; internal set; }
    }

    public static Result Scan(string root, Func<string, bool>? skipDirectory = null,
        bool preferStreamlineIndicator = true, int? maxPathLength = null)
    {
        var result = new Result();
        if (!Directory.Exists(root)) return result;
        Search(root, 0);
        return result;

        void Search(string directory, int depth)
        {
            if (depth > 8 || (maxPathLength is int limit && directory.Length > limit)) return;
            if (depth > 0 && skipDirectory?.Invoke(Path.GetFileName(directory)) == true) return;
            try
            {
                var files = Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.Ordinal).ToList();
                var optiScaler = files.Any(f => Path.GetFileName(f).Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (DllNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (optiScaler) result.OptiScalerDlls[name] = file;
                        else result.GameDlls.TryAdd(name, file);
                    }
                    if (name.Equals(StreamlineIndicator, StringComparison.OrdinalIgnoreCase) && result.StreamlineIndicatorPath == null)
                    {
                        result.StreamlineIndicatorPath = file;
                        if (preferStreamlineIndicator) result.StreamlineFolder = directory;
                    }
                    if (result.StreamlineFolder == null && StreamlineDlls.Contains(name, StringComparer.OrdinalIgnoreCase))
                        result.StreamlineFolder = directory;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
                {
                    if (new DirectoryInfo(child).LinkTarget != null) continue;
                    Search(child, depth + 1);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
    }
}
