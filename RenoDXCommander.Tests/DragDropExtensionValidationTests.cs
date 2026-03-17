using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for DragDropHandler.IsAllowedExtension and AllowedExtensions.
/// Validates: Requirements 12.1, 12.2
/// </summary>
public class DragDropExtensionValidationTests
{
    [Theory]
    [InlineData(@"C:\Games\MyGame.exe")]
    [InlineData(@"C:\Downloads\mod.addon64")]
    [InlineData(@"C:\Downloads\mod.addon32")]
    [InlineData(@"C:\Downloads\archive.zip")]
    [InlineData(@"C:\Downloads\archive.7z")]
    [InlineData(@"C:\Downloads\archive.rar")]
    [InlineData(@"C:\Downloads\archive.tar")]
    [InlineData(@"C:\Downloads\archive.gz")]
    [InlineData(@"C:\Downloads\archive.bz2")]
    [InlineData(@"C:\Downloads\archive.xz")]
    [InlineData(@"C:\Downloads\archive.tgz")]
    public void IsAllowedExtension_AllowedExtensions_ReturnsTrue(string path)
    {
        Assert.True(DragDropHandler.IsAllowedExtension(path));
    }

    [Theory]
    [InlineData(@"C:\Downloads\readme.txt")]
    [InlineData(@"C:\Downloads\image.png")]
    [InlineData(@"C:\Downloads\document.pdf")]
    [InlineData(@"C:\Downloads\script.bat")]
    [InlineData(@"C:\Downloads\library.dll")]
    [InlineData(@"C:\Downloads\config.json")]
    [InlineData(@"C:\Downloads\data.xml")]
    public void IsAllowedExtension_DisallowedExtensions_ReturnsFalse(string path)
    {
        Assert.False(DragDropHandler.IsAllowedExtension(path));
    }

    [Theory]
    [InlineData(@"C:\Games\MyGame.EXE")]
    [InlineData(@"C:\Downloads\mod.ADDON64")]
    [InlineData(@"C:\Downloads\archive.ZIP")]
    [InlineData(@"C:\Downloads\archive.Rar")]
    public void IsAllowedExtension_CaseInsensitive(string path)
    {
        Assert.True(DragDropHandler.IsAllowedExtension(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedExtension_NullOrEmpty_ReturnsFalse(string? path)
    {
        Assert.False(DragDropHandler.IsAllowedExtension(path));
    }

    [Fact]
    public void IsAllowedExtension_NoExtension_ReturnsFalse()
    {
        Assert.False(DragDropHandler.IsAllowedExtension(@"C:\Games\NoExtension"));
    }

    [Theory]
    [InlineData(@"C:\Ünïcödé\Gàmé.exe")]
    [InlineData(@"C:\日本語\ゲーム.zip")]
    [InlineData(@"C:\Spëcîal Çhàrs\my game (2024).addon64")]
    public void IsAllowedExtension_UnicodeAndSpecialChars_HandledGracefully(string path)
    {
        Assert.True(DragDropHandler.IsAllowedExtension(path));
    }

    [Theory]
    [InlineData(@"C:\Ünïcödé\readme.txt")]
    [InlineData(@"C:\日本語\ドキュメント.pdf")]
    public void IsAllowedExtension_UnicodeWithDisallowedExtension_ReturnsFalse(string path)
    {
        Assert.False(DragDropHandler.IsAllowedExtension(path));
    }

    [Fact]
    public void AllowedExtensions_ContainsExactlyExpectedSet()
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".addon64", ".addon32",
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz",
        };

        Assert.Equal(expected.Count, DragDropHandler.AllowedExtensions.Count);
        foreach (var ext in expected)
        {
            Assert.Contains(ext, DragDropHandler.AllowedExtensions);
        }
    }
}
