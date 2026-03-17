using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for version display in status text and color.
/// Feature: version-display-and-mod-authors
/// </summary>
public class VersionDisplayPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    private static readonly Gen<string?> GenNullableVersion =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements("1.0.0", "5.9.2", "2.3.1", "0.1.0", "10.20.30", "3.0.0-beta"));

    private static readonly Gen<string> GenNonNullVersion =
        Gen.Elements("1.0.0", "5.9.2", "2.3.1", "0.1.0", "10.20.30", "3.0.0-beta");

    // ── Property 1: Update-available status text shows installed version ───────────
    // Feature: version-display-and-mod-authors, Property 1: Update-available status text shows installed version
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    [Property(MaxTest = 100)]
    public Property UpdateAvailable_StatusText_ShowsInstalledVersion()
    {
        return Prop.ForAll(
            Arb.From(GenNullableVersion),
            (string? version) =>
            {
                var card = new GameCardViewModel
                {
                    RsStatus = GameStatus.UpdateAvailable,
                    RsInstalledVersion = version,
                    DcStatus = GameStatus.UpdateAvailable,
                    DcInstalledVersion = version
                };

                string expectedText = version ?? "Update";
                bool rsCorrect = card.RsStatusText == expectedText;
                bool dcCorrect = card.DcStatusText == expectedText;

                return rsCorrect && dcCorrect;
            });
    }

    // ── Property 2: Update-available status color is always purple ─────────────────
    // Feature: version-display-and-mod-authors, Property 2: Update-available status color is always purple
    // **Validates: Requirements 1.5, 1.6**
    [Property(MaxTest = 100)]
    public Property UpdateAvailable_StatusColor_AlwaysPurple()
    {
        return Prop.ForAll(
            Arb.From(GenNullableVersion),
            (string? version) =>
            {
                var card = new GameCardViewModel
                {
                    RsStatus = GameStatus.UpdateAvailable,
                    RsInstalledVersion = version,
                    DcStatus = GameStatus.UpdateAvailable,
                    DcInstalledVersion = version
                };

                bool rsCorrect = card.RsStatusColor == "#B898E8";
                bool dcCorrect = card.DcStatusColor == "#B898E8";

                return rsCorrect && dcCorrect;
            });
    }

    // ── Property 3: Installed status shows version in green ────────────────────────
    // Feature: version-display-and-mod-authors, Property 3: Installed status shows version in green
    // **Validates: Requirements 1.7**
    [Property(MaxTest = 100)]
    public Property Installed_StatusText_ShowsVersionInGreen()
    {
        return Prop.ForAll(
            Arb.From(GenNonNullVersion),
            (string version) =>
            {
                var card = new GameCardViewModel
                {
                    RsStatus = GameStatus.Installed,
                    RsInstalledVersion = version,
                    DcStatus = GameStatus.Installed,
                    DcInstalledVersion = version
                };

                bool rsTextCorrect = card.RsStatusText == version;
                bool dcTextCorrect = card.DcStatusText == version;
                bool rsColorCorrect = card.RsStatusColor == "#5ECB7D";
                bool dcColorCorrect = card.DcStatusColor == "#5ECB7D";

                return rsTextCorrect && dcTextCorrect && rsColorCorrect && dcColorCorrect;
            });
    }
}
