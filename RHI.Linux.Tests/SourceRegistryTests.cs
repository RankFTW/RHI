using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class SourceRegistryTests
{
    [Fact] public void LinuxDownloadSourcesAreOwnedBySharedRegistryOrServices()
    {
        // These are user-facing help links, not component download sources.
        var helpLinks = new HashSet<string>(StringComparer.Ordinal)
        {
            "https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases",
            "https://github.com/optiscaler/OptiScaler/wiki",
            "https://discord.com/channels/1408098019194310818/1543802634991968366",
            "https://github.com/NIGos/dlss5-bridge",
            "https://discord.com/channels/1408098019194310818/1543975158937821315",
            "https://github.com/jlrouzies-fr/DLSS5-Feeder",
        };
        var core = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../RHI.Linux.Core"));
        var scattered = Directory.EnumerateFiles(core, "*.cs")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "https?://[^\\\"\\s]+")
                .Select(match => (File: Path.GetFileName(file), Url: match.Value)))
            .Where(item => !helpLinks.Contains(item.Url));
        Assert.Empty(scattered);
    }

    private static string SourceFile([CallerFilePath] string file = "") => file;
}
