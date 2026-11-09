using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Locked hidden achievements stay "Hidden achievement" unless the user turns on Show hidden.</summary>
public class AchievementMaskTests
{
    private static Achievement A(bool hidden, bool unlocked, string name = "Secret Boss") =>
        new("API", name, "Beat the secret boss.", hidden, unlocked, null, null, null);

    [Fact]
    public void LockedHidden_MaskedUntilShowHidden()
    {
        Assert.True(A(hidden: true, unlocked: false).IsMasked(showHidden: false));
        Assert.False(A(hidden: true, unlocked: false).IsMasked(showHidden: true));
    }

    [Fact]
    public void UnlockedOrNotHidden_NeverMasked()
    {
        Assert.False(A(hidden: true, unlocked: true).IsMasked(false));
        Assert.False(A(hidden: false, unlocked: false).IsMasked(false));
    }

    [Fact]
    public void NoNameFromTheStore_StaysMasked()
    {
        Assert.True(A(hidden: true, unlocked: false, name: "").IsMasked(showHidden: true));
    }
}
