using RenoDXCommander.Collections;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Game collections: the store (names, membership, order, rename migration) and the list filter.</summary>
public class GameCollectionTests
{
    private const string Forza = "Forza Horizon 5|Steam";
    private const string Dirt = "DiRT Rally 2.0|Steam";

    private static GameCollectionStore StoreWith(params string[] names)
    {
        var store = new GameCollectionStore();
        foreach (var n in names) Assert.Null(store.Create(n, out _));
        return store;
    }

    [Fact]
    public void Membership_AddRemoveToggle()
    {
        var store = StoreWith("Racing");
        Assert.True(store.SetMembership("Racing", Forza, true));
        Assert.False(store.SetMembership("Racing", Forza, true)); // already in
        Assert.True(store.Contains("racing", "forza horizon 5|steam")); // case-insensitive

        Assert.True(store.Toggle("Racing", Dirt));
        Assert.False(store.Toggle("Racing", Dirt));
        Assert.Equal(new[] { Forza }, store.GetMembers("Racing")!.ToArray());

        Assert.True(store.SetMembership("Racing", Forza, false));
        Assert.Empty(store.GetMembers("Racing")!);
    }

    [Fact]
    public void Game_CanBeInSeveralCollections()
    {
        var store = StoreWith("Racing", "Favourite racers");
        store.SetMembership("Racing", Forza, true);
        store.SetMembership("Favourite racers", Forza, true);
        Assert.Equal(new[] { "Racing", "Favourite racers" }, store.CollectionsContaining(Forza));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Installed", false)]
    [InlineData("favourites", false)]
    [InlineData("All", false)]
    [InlineData("Hidden", false)]
    [InlineData("Detected", false)]
    [InlineData("RACING", false)]           // already used (ignoring case)
    [InlineData("Souls-likes", true)]
    [InlineData("  Co-op  ", true)]         // trimmed
    public void ValidateName_NewCollection(string name, bool ok)
    {
        var store = StoreWith("Racing");
        var error = store.ValidateName(name, null, out var clean);
        Assert.Equal(ok, error == null);
        if (ok) Assert.Equal(name.Trim(), clean);
    }

    [Fact]
    public void ValidateName_TooLong_Rejected()
    {
        var store = new GameCollectionStore();
        Assert.NotNull(store.ValidateName(new string('a', GameCollectionStore.MaxNameLength + 1), null, out _));
        Assert.Null(store.ValidateName(new string('a', GameCollectionStore.MaxNameLength), null, out _));
    }

    [Fact]
    public void Rename_KeepsGamesAndActive_AllowsCaseChange()
    {
        var store = StoreWith("Racing", "RPG");
        store.SetMembership("Racing", Forza, true);
        store.ActiveName = "Racing";

        Assert.NotNull(store.Rename("Racing", "rpg", out _)); // taken by another collection
        Assert.Null(store.Rename("Racing", "racing", out _));  // own name, new case
        Assert.Null(store.Rename("racing", "Driving", out var renamed));
        Assert.Equal("Driving", renamed);
        Assert.Equal("Driving", store.ActiveName);
        Assert.True(store.Contains("Driving", Forza));
        Assert.False(store.Exists("Racing"));
    }

    [Fact]
    public void Delete_RemovesOnlyTheCollection()
    {
        var store = StoreWith("Racing", "RPG");
        store.SetMembership("Racing", Forza, true);
        store.SetMembership("RPG", Forza, true);
        store.ActiveName = "Racing";

        Assert.True(store.Delete("Racing"));
        Assert.Null(store.ActiveName);
        Assert.Equal(new[] { "RPG" }, store.Names);
        Assert.True(store.Contains("RPG", Forza)); // the game is untouched elsewhere
        Assert.False(store.Delete("Racing"));
    }

    [Fact]
    public void Move_ReordersAndClamps()
    {
        var store = StoreWith("A1", "B2", "C3");
        Assert.True(store.Move("C3", -1));
        Assert.Equal(new[] { "A1", "C3", "B2" }, store.Names);
        Assert.False(store.Move("A1", -1)); // already first
        Assert.True(store.Move("A1", 5));
        Assert.Equal(new[] { "C3", "B2", "A1" }, store.Names);
    }

    [Fact]
    public void GameRename_MigratesMembershipForEveryStore()
    {
        var store = StoreWith("Racing");
        store.SetMembership("Racing", "Forza|Steam", true);
        store.SetMembership("Racing", "Forza|Xbox", true);
        store.SetMembership("Racing", Dirt, true);

        store.MigrateGameRename("forza", "Forza Horizon 5");

        var members = store.GetMembers("Racing")!;
        Assert.Contains("Forza Horizon 5|Steam", members);
        Assert.Contains("Forza Horizon 5|Xbox", members);
        Assert.Contains(Dirt, members);
        Assert.DoesNotContain("Forza|Steam", members);
    }

    [Fact]
    public void GameRename_ThroughGameNameServiceHelperKeysMatch()
    {
        // Same key format as the other composite-keyed sets RenameGame migrates
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Forza|Steam" };
        GameNameService.MigrateCompositeHashSet(set, "Forza", "Forza Horizon 5");
        var store = StoreWith("Racing");
        store.SetMembership("Racing", "Forza|Steam", true);
        store.MigrateGameRename("Forza", "Forza Horizon 5");
        Assert.Equal(set.Single(), store.GetMembers("Racing")!.Single());
    }

    [Fact]
    public void Load_DropsBadEntriesAndUnknownActive()
    {
        var store = new GameCollectionStore();
        store.Load(new[]
        {
            new GameCollection { Name = "Racing", Games = new() { Forza, "forza horizon 5|STEAM", "" } },
            new GameCollection { Name = "racing", Games = new() { Dirt } }, // duplicate name
            new GameCollection { Name = "Installed" },                      // built-in name
            new GameCollection { Name = "  " },
        }, "Gone");
        Assert.Equal(new[] { "Racing" }, store.Names);
        Assert.Single(store.GetMembers("Racing")!);
        Assert.Null(store.ActiveName);

        store.Load(store.Snapshot(), "RACING");
        Assert.Equal("Racing", store.ActiveName);
    }

    [Fact]
    public void Filter_ShowsCollectionGames_ExcludingHidden()
    {
        var store = StoreWith("Racing");
        store.SetMembership("Racing", Forza, true);
        store.SetMembership("Racing", Dirt, true);

        var forza = new GameCardViewModel { GameName = "Forza Horizon 5", Source = "Steam" };
        var dirt = new GameCardViewModel { GameName = "DiRT Rally 2.0", Source = "Steam", IsHidden = true };
        var other = new GameCardViewModel { GameName = "Elden Ring", Source = "Steam" };
        var forzaXbox = new GameCardViewModel { GameName = "Forza Horizon 5", Source = "Xbox" }; // other store, not added

        var shown = new BatchObservableCollection<GameCardViewModel>();
        var filter = new FilterViewModel { CollectionMembers = store.GetMembers };
        filter.Initialize(shown);
        filter.SetAllCards(new[] { forza, dirt, other, forzaXbox });

        filter.ActivateCollection("Racing");
        Assert.Equal("Racing", filter.ActiveCollectionName);
        Assert.Equal(new[] { forza }, shown.ToArray());

        // A main filter clears the collection
        filter.SetFilter("Detected");
        Assert.Null(filter.ActiveCollectionName);
        Assert.Equal(3, shown.Count);

        // A collection that doesn't exist isn't activated
        filter.ActivateCollection("Nope");
        Assert.Null(filter.ActiveCollectionName);
    }

    [Fact]
    public void Filter_SearchNarrowsWithinCollection()
    {
        var store = StoreWith("Racing");
        store.SetMembership("Racing", Forza, true);
        store.SetMembership("Racing", Dirt, true);
        var forza = new GameCardViewModel { GameName = "Forza Horizon 5", Source = "Steam" };
        var dirt = new GameCardViewModel { GameName = "DiRT Rally 2.0", Source = "Steam" };

        var shown = new BatchObservableCollection<GameCardViewModel>();
        var filter = new FilterViewModel { CollectionMembers = store.GetMembers };
        filter.Initialize(shown);
        filter.SetAllCards(new[] { forza, dirt });
        filter.ActivateCollection("Racing");
        Assert.Equal(2, shown.Count);

        filter.SearchQuery = "rally";
        Assert.Equal(new[] { dirt }, shown.ToArray());
    }
}
