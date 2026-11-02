using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>PCGW config paths (often several joined with ", ") become usable '|' candidates.</summary>
public class PcgwConfigPathTests
{
    [Fact]
    public void Drops_placeholder_entry_and_keeps_appdata_config()
    {
        var r = AuxInstallService.PcgwConfigPathToCandidates(
            @"%LOCALAPPDATA%\ProjectPBZ\Saved\Config\Windows\, <path-to-game>\ProjectPBZ\Saved\");
        Assert.Equal(@"%LOCALAPPDATA%\ProjectPBZ\Saved\Config\Windows", r);
    }

    [Fact]
    public void Prefers_config_folders_over_docs_folders()
    {
        var r = AuxInstallService.PcgwConfigPathToCandidates(
            @"%USERPROFILE%\Documents\My Games\A Way Out\, %LOCALAPPDATA%\Haze1\Saved\Config\WindowsNoEditor\");
        Assert.Equal(@"%LOCALAPPDATA%\Haze1\Saved\Config\WindowsNoEditor", r);
    }

    [Fact]
    public void Keeps_every_location_when_none_is_a_config_folder()
    {
        var r = AuxInstallService.PcgwConfigPathToCandidates(
            @"%LOCALAPPDATA%\PioneerGame\Saved, %LOCALAPPDATA%\PioneerGame_Entrophy\Saved");
        Assert.Equal(@"%LOCALAPPDATA%\PioneerGame\Saved|%LOCALAPPDATA%\PioneerGame_Entrophy\Saved", r);
    }

    [Fact]
    public void Strips_file_names_and_wildcards()
    {
        Assert.Equal(@"%USERPROFILE%\Documents\WB Games\Batman Arkham Knight",
            AuxInstallService.PcgwConfigPathToCandidates(
                @"<path-to-game>\BMGame\Config\, %USERPROFILE%\Documents\WB Games\Batman Arkham Knight\GFXSettings.BatmanArkhamKnight.xml"));
        Assert.Equal(@"%USERPROFILE%\Documents\My Games\X\Config",
            AuxInstallService.PcgwConfigPathToCandidates(@"%USERPROFILE%\Documents\My Games\X\Config\*.ini"));
    }

    [Fact]
    public void Single_path_is_unchanged_apart_from_trailing_slash()
    {
        Assert.Equal(@"%LOCALAPPDATA%\TheQuarry\Saved\Config\WindowsNoEditor",
            AuxInstallService.PcgwConfigPathToCandidates(@"%LOCALAPPDATA%\TheQuarry\Saved\Config\WindowsNoEditor\"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"<path-to-game>\Saved\Config")]
    public void Unusable_input_returns_null(string? raw) =>
        Assert.Null(AuxInstallService.PcgwConfigPathToCandidates(raw));
}
