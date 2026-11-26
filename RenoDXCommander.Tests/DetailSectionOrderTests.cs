using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Saved section orders: the Experimental section (v2.9.6) slots in just before Management, and the
/// Game Overrides section (removed in 2.9.6.3) is dropped.
/// </summary>
public class DetailSectionOrderTests
{
    private static List<string> Load(string json)
    {
        var vm = new SettingsViewModel();
        vm.LoadSettingsFromDict(new Dictionary<string, string> { ["DetailSectionOrder"] = json });
        return vm.DetailSectionOrder;
    }

    [Fact]
    public void Default_HasExperimental_BeforeManagement()
    {
        var order = SettingsViewModel.DefaultSectionOrder.ToList();
        Assert.Equal(order.IndexOf("Management") - 1, order.IndexOf("Experimental"));
    }

    [Fact]
    public void SavedOrder_WithoutExperimental_GetsItBeforeManagement()
    {
        var order = Load("[\"Extras\",\"Management\",\"Components\",\"GameOverrides\",\"NeuralRendering\",\"NvidiaProfileDlss\",\"NvidiaProfileDriver\"]");
        Assert.Equal(new[] { "Extras", "Experimental", "Management", "Components", "NeuralRendering", "NvidiaProfileDlss", "NvidiaProfileDriver" }, order);
    }

    [Fact]
    public void SavedOrder_WithExperimental_IsKept()
    {
        var order = Load("[\"Experimental\",\"Components\",\"GameOverrides\",\"NeuralRendering\",\"NvidiaProfileDlss\",\"NvidiaProfileDriver\",\"Management\",\"Extras\"]");
        Assert.Equal("Experimental", order[0]);
        Assert.Equal(7, order.Count);
    }

    [Fact]
    public void GameOverrides_IsNotASection()
    {
        Assert.DoesNotContain("GameOverrides", SettingsViewModel.DefaultSectionOrder);
        Assert.DoesNotContain("GameOverrides", Load("[\"GameOverrides\",\"Components\",\"Extras\"]"));
    }

    [Fact]
    public void SavedCollapseState_ForGameOverrides_IsDropped()
    {
        var vm = new SettingsViewModel();
        vm.LoadSettingsFromDict(new Dictionary<string, string> { ["CollapsedDetailSections"] = "[\"GameOverrides\",\"Extras\"]" });
        Assert.Equal(new[] { "Extras" }, vm.CollapsedDetailSections.ToArray());
    }
}