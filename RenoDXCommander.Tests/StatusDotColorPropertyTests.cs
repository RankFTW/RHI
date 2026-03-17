using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GameCardViewModel status dot color properties.
/// </summary>
public class StatusDotColorPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    /// <summary>
    /// Generates a GameCardViewModel with arbitrary status and installing flags
    /// for all four components (RenoDX, ReShade, Display Commander, Luma).
    /// </summary>
    private static readonly Gen<GameCardViewModel> GenCard =
        from rdxStatus in GenStatus
        from rsStatus in GenStatus
        from dcStatus in GenStatus
        from lumaStatus in GenStatus
        from isInstalling in Arb.Default.Bool().Generator
        from rsInstalling in Arb.Default.Bool().Generator
        from dcInstalling in Arb.Default.Bool().Generator
        from lumaInstalling in Arb.Default.Bool().Generator
        select new GameCardViewModel
        {
            GameName = "TestGame",
            Status = rdxStatus,
            RsStatus = rsStatus,
            DcStatus = dcStatus,
            LumaStatus = lumaStatus,
            IsInstalling = isInstalling,
            RsIsInstalling = rsInstalling,
            DcIsInstalling = dcInstalling,
            IsLumaInstalling = lumaInstalling,
            EngineHint = ""
        };

    private static readonly Regex HexPattern = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    // ── Property 7: StatusDotColor produces valid hex for all states ───────────────
    // Feature: codebase-optimization, Property 7: StatusDotColor produces valid hex for all states
    // **Validates: Requirements 10.6**
    [Property(MaxTest = 100)]
    public Property StatusDotColors_AreValidHex_ForAllStatesAndInstallingFlags()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            (GameCardViewModel card) =>
            {
                var errors = new List<string>();

                void Check(string name, string value)
                {
                    if (string.IsNullOrEmpty(value) || !HexPattern.IsMatch(value))
                        errors.Add($"{name} = \"{value}\"");
                }

                Check(nameof(card.CardRdxStatusDot), card.CardRdxStatusDot);
                Check(nameof(card.CardRsStatusDot), card.CardRsStatusDot);
                Check(nameof(card.CardDcStatusDot), card.CardDcStatusDot);
                Check(nameof(card.CardLumaStatusDot), card.CardLumaStatusDot);

                return (errors.Count == 0)
                    .Label($"Invalid hex colors: [{string.Join(", ", errors)}]");
            });
    }
}
