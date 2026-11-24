using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>The Experimental section (v2.9.6) slots into saved section orders just before Management.</summary>
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
        Assert.Equal(new[] { "Extras", "Experimental", "Management", "Components", "GameOverrides", "NeuralRendering", "NvidiaProfileDlss", "NvidiaProfileDriver" }, order);
    }

    [Fact]
    public void SavedOrder_WithExperimental_IsKept()
    {
        var order = Load("[\"Experimental\",\"Components\",\"GameOverrides\",\"NeuralRendering\",\"NvidiaProfileDlss\",\"NvidiaProfileDriver\",\"Management\",\"Extras\"]");
        Assert.Equal("Experimental", order[0]);
        Assert.Equal(8, order.Count);
    }
}