using System.IO;
using Claxe.Models;
using Claxe.Services;

namespace Sorune.Tests;

public class TimeMachineTests
{
    [Fact]
    public void LastClosedComesBackFirstAndSurvivesARestart()
    {
        var store = new TimeMachineStore();
        store.Clear();
        store.AddClosed(new ClosedTab { Url = "https://a.example/", Title = "A", Index = 0 });
        store.AddClosed(new ClosedTab { Url = "https://b.example/", Title = "B", Index = 3, GroupName = "İş", GroupColor = "red" });

        var reloaded = new TimeMachineStore(); // read back from the encrypted file
        var latest = reloaded.TakeLatest();
        Assert.Equal("https://b.example/", latest?.Url);
        Assert.Equal(3, latest?.Index);
        Assert.Equal("İş", latest?.GroupName);
        Assert.Equal("https://a.example/", reloaded.TakeLatest()?.Url);
        Assert.Null(reloaded.TakeLatest());
    }

    [Fact]
    public void KeepsAtMostTheLimit()
    {
        var store = new TimeMachineStore();
        store.Clear();
        for (var i = 0; i < TimeMachineStore.MaxClosed + 15; i++)
            store.AddClosed(new ClosedTab { Url = $"https://x.example/{i}" });
        Assert.Equal(TimeMachineStore.MaxClosed, store.Closed.Count);
        Assert.Equal($"https://x.example/{TimeMachineStore.MaxClosed + 14}", store.Closed[0].Url);
    }

    [Fact]
    public void SameSessionIsArchivedOnce()
    {
        var store = new TimeMachineStore();
        store.Clear();
        var session = new SessionState
        {
            Tabs =
            [
                new SessionTab { Url = "https://a.example/", Title = "A" },
                new SessionTab { Url = InternalPages.NewTabUrl, Title = "New" },
            ],
        };
        store.ArchiveSession(session, DateTime.Now);
        store.ArchiveSession(session, DateTime.Now);
        Assert.Single(store.Sessions);
        Assert.Single(store.Sessions[0].Tabs); // empty new tabs are not worth keeping
    }
}

public class ProfileTests
{
    [Fact]
    public void CreateRenameAndDeleteAProfile()
    {
        var profile = ProfileService.Create("  Oyun  ", "🎮", "#5BD68B");
        Assert.NotNull(profile);
        Assert.Equal("Oyun", profile!.Name);
        Assert.True(Directory.Exists(ProfileService.DataRootOf(profile.Id)));
        Assert.Contains(ProfileService.List(), p => p.Id == profile.Id);

        Assert.True(ProfileService.Update(profile.Id, "Oyunlar", null, "#FF6B7A"));
        var updated = ProfileService.List().Single(p => p.Id == profile.Id);
        Assert.Equal("Oyunlar", updated.Name);
        Assert.Equal("#FF6B7A", updated.Color);

        Assert.True(ProfileService.Delete(profile.Id));
        Assert.DoesNotContain(ProfileService.List(), p => p.Id == profile.Id);
        Assert.False(Directory.Exists(ProfileService.DataRootOf(profile.Id)));
    }

    [Fact]
    public void TheMainProfileCannotBeDeleted()
    {
        Assert.False(ProfileService.Delete(ProfileService.DefaultId));
        Assert.Contains(ProfileService.List(), p => p.Id == ProfileService.DefaultId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProfilesNeedAName(string name) => Assert.Null(ProfileService.Create(name, null, null));

    [Fact]
    public void BadIdsAreRefused() => Assert.False(ProfileService.Delete("..\\..\\Windows"));
}
