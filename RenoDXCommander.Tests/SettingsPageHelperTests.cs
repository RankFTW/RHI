using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Settings page helpers: the update countdown texts and search result excerpts.
/// </summary>
public class SettingsPageHelperTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NextCheck_BeforeTimerStarts_ShowsAfterStartup()
        => Assert.Equal("After startup", SettingsHandler.FormatNextUpdateCheck(null, Now));

    [Fact]
    public void NextCheck_FormatsHoursMinutesSeconds()
        => Assert.Equal("2h 41m 20s", SettingsHandler.FormatNextUpdateCheck(Now + new TimeSpan(2, 41, 20), Now));

    [Fact]
    public void NextCheck_Overdue_ShowsAnyMoment()
        => Assert.Equal("Any moment", SettingsHandler.FormatNextUpdateCheck(Now.AddSeconds(-5), Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void LastCheck_EmptyOrUnparsable_ShowsNotYet(string? value)
        => Assert.Equal("Not yet", SettingsHandler.FormatLastUpdateCheck(value, Now));

    [Fact]
    public void LastCheck_FormatsAgo()
    {
        Assert.Equal("Just now", SettingsHandler.FormatLastUpdateCheck(Now.AddSeconds(-20).ToString("o"), Now));
        Assert.Equal("Just now", SettingsHandler.FormatLastUpdateCheck(Now.AddMinutes(5).ToString("o"), Now));
        Assert.Equal("18m ago", SettingsHandler.FormatLastUpdateCheck(Now.AddMinutes(-18).ToString("o"), Now));
        Assert.Equal("1h 18m ago", SettingsHandler.FormatLastUpdateCheck(Now.AddMinutes(-78).ToString("o"), Now));
        Assert.Equal("2d 3h ago", SettingsHandler.FormatLastUpdateCheck(Now.AddHours(-51).ToString("o"), Now));
    }

    [Fact]
    public void Excerpt_ShortTextUnchanged_LongTextCentredOnMatch()
    {
        Assert.Equal("Peak nits", SettingsHandler.Excerpt("Peak nits", "nits", 40));
        var text = new string('a', 100) + " ReBAR " + new string('b', 100);
        var ex = SettingsHandler.Excerpt(text, "rebar", 40);
        Assert.Contains("ReBAR", ex);
        Assert.StartsWith("…", ex);
        Assert.EndsWith("…", ex);
    }

    // ── First periodic check delay ─────────────────────────────────────────────

    [Fact]
    public void FirstCheck_LastCheckRecent_DueAtLastPlusFourHours()
        => Assert.Equal(new TimeSpan(1, 16, 0),
            ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(Now - new TimeSpan(2, 44, 0), Now, minimalMode: false));

    [Fact]
    public void FirstCheck_JustChecked_FullInterval()
        => Assert.Equal(TimeSpan.FromHours(4),
            ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(Now, Now, minimalMode: false));

    [Fact]
    public void FirstCheck_Overdue_UsesMinimumDelay()
        => Assert.Equal(TimeSpan.FromMinutes(5),
            ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(Now.AddHours(-9), Now, minimalMode: false));

    [Fact]
    public void FirstCheck_NoLastCheckOrFuture_FullInterval()
    {
        Assert.Equal(TimeSpan.FromHours(4), ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(null, Now, false));
        Assert.Equal(TimeSpan.FromHours(4), ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(Now.AddHours(2), Now, false));
    }

    [Fact]
    public void FirstCheck_MinimalMode_FullInterval()
        => Assert.Equal(TimeSpan.FromHours(4),
            ViewModels.MainViewModel.ComputeFirstPeriodicCheckDelay(Now.AddHours(-3), Now, minimalMode: true));
}
