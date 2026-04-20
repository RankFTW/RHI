using Microsoft.UI.Xaml;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for GameCardViewModel Nexus Mods properties.
/// Validates computed visibility properties driven by Nexus state.
/// </summary>
public class NexusGameCardPropertyTests
{
    // ── Badge visibility: IsNexusUpdateAvailable ──────────────────────────────────

    /// <summary>
    /// Validates: Requirements 4.1
    /// WHEN IsNexusUpdateAvailable is true, NexusUpdateBadgeVisibility SHALL be Visible.
    /// </summary>
    [Fact]
    public void NexusUpdateBadge_Visible_WhenUpdateAvailable()
    {
        var card = new GameCardViewModel
        {
            IsNexusUpdateAvailable = true
        };

        Assert.Equal(Visibility.Visible, card.NexusUpdateBadgeVisibility);
    }

    /// <summary>
    /// Validates: Requirements 4.1
    /// WHEN IsNexusUpdateAvailable is false, NexusUpdateBadgeVisibility SHALL be Collapsed.
    /// </summary>
    [Fact]
    public void NexusUpdateBadge_Collapsed_WhenNoUpdateAvailable()
    {
        var card = new GameCardViewModel
        {
            IsNexusUpdateAvailable = false
        };

        Assert.Equal(Visibility.Collapsed, card.NexusUpdateBadgeVisibility);
    }

    // ── Download progress visibility: IsNexusDownloading ──────────────────────────

    /// <summary>
    /// Validates: Requirements 4.3
    /// WHEN IsNexusDownloading is true, NexusProgressVisibility SHALL be Visible.
    /// </summary>
    [Fact]
    public void NexusProgress_Visible_WhenDownloading()
    {
        var card = new GameCardViewModel
        {
            IsNexusDownloading = true
        };

        Assert.Equal(Visibility.Visible, card.NexusProgressVisibility);
    }

    /// <summary>
    /// Validates: Requirements 4.3
    /// WHEN IsNexusDownloading is false, NexusProgressVisibility SHALL be Collapsed.
    /// </summary>
    [Fact]
    public void NexusProgress_Collapsed_WhenNotDownloading()
    {
        var card = new GameCardViewModel
        {
            IsNexusDownloading = false
        };

        Assert.Equal(Visibility.Collapsed, card.NexusProgressVisibility);
    }

    // ── Action message visibility: NexusActionMessage ─────────────────────────────

    /// <summary>
    /// Validates: Requirements 4.3
    /// WHEN NexusActionMessage is a non-empty string, NexusMessageVisibility SHALL be Visible.
    /// </summary>
    [Fact]
    public void NexusMessage_Visible_WhenMessagePresent()
    {
        var card = new GameCardViewModel
        {
            NexusActionMessage = "Downloading update..."
        };

        Assert.Equal(Visibility.Visible, card.NexusMessageVisibility);
    }

    /// <summary>
    /// Validates: Requirements 4.3
    /// WHEN NexusActionMessage is empty, NexusMessageVisibility SHALL be Collapsed.
    /// </summary>
    [Fact]
    public void NexusMessage_Collapsed_WhenMessageEmpty()
    {
        var card = new GameCardViewModel
        {
            NexusActionMessage = ""
        };

        Assert.Equal(Visibility.Collapsed, card.NexusMessageVisibility);
    }

    // ── IsNexusNotDownloading inverse property ────────────────────────────────────

    /// <summary>
    /// Validates: Requirements 4.3
    /// IsNexusNotDownloading SHALL be the inverse of IsNexusDownloading.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsNexusNotDownloading_IsInverseOfIsNexusDownloading(bool downloading, bool expectedNotDownloading)
    {
        var card = new GameCardViewModel
        {
            IsNexusDownloading = downloading
        };

        Assert.Equal(expectedNotDownloading, card.IsNexusNotDownloading);
    }
}
