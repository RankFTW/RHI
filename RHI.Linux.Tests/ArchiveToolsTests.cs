using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class ArchiveToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-extractor-" + Guid.NewGuid().ToString("N"));

    private string Tool(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void RelocatedBundleWinsOverHostToolAndWorksWithEmptyPath()
    {
        var bundled = Tool("portable path 100%/tools/7zz");
        Tool("bin/7z");
        var app = Path.GetDirectoryName(Path.GetDirectoryName(bundled))!;
        Assert.Equal(bundled, ArchiveTools.ResolveSevenZip(app, Path.Combine(_root, "bin")));
        Assert.Equal(bundled, ArchiveTools.ResolveSevenZip(app, ""));
    }

    [Fact]
    public void SourceRunFindsHostExtractorOrReportsBuildInstructions()
    {
        var tool = Tool("bin/7z");
        Assert.Equal(tool, ArchiveTools.ResolveSevenZip(_root, Path.GetDirectoryName(tool)));
        var error = Assert.Throws<IOException>(() => ArchiveTools.ResolveSevenZip(_root, ""));
        Assert.Contains("build-linux.sh", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
