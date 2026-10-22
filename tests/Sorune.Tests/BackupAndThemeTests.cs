using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Claxe.Models;
using Claxe.Services;

namespace Sorune.Tests;

[Collection(TestProfile.DataRootCollection)]
public class UserThemeStoreTests
{
    [Fact]
    public void SavedThemeKeepsItsOwnCopyOfACustomWallpaper()
    {
        Directory.CreateDirectory(ThemeService.CustomWallpapersFolder);
        File.WriteAllBytes(Path.Combine(ThemeService.CustomWallpapersFolder, "custom-test.png"), [1, 2, 3]);
        var settings = new AppSettings { Theme = "midnight-mei", Wallpaper = "custom:custom-test.png", AccentColor = "#FF4D6A", SoundPack = "soft" };

        var store = new UserThemeStore();
        var theme = store.Save("  " + new string('a', 40) + "  ", settings);

        Assert.NotNull(theme);
        Assert.Equal(30, theme.Name.Length);
        Assert.Equal("#FF4D6A", theme.AccentColor);
        Assert.StartsWith("custom:theme-", theme.Wallpaper);
        var copy = Path.Combine(ThemeService.CustomWallpapersFolder, theme.Wallpaper!["custom:".Length..]);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(copy));

        // Read back from disk, then deleting removes the copy too.
        var reloaded = new UserThemeStore();
        Assert.NotNull(reloaded.Find(theme.Id));
        reloaded.Delete(theme.Id);
        Assert.False(File.Exists(copy));
        Assert.Null(new UserThemeStore().Find(theme.Id));
    }

    [Fact]
    public void ANameIsRequired()
        => Assert.Null(new UserThemeStore().Save("   ", new AppSettings()));
}

[Collection(TestProfile.DataRootCollection)]
public class BackupTests
{
    private static string Data(string name) => Path.Combine(AppPaths.DataRoot, name);

    [Fact]
    public void BackupRestoresSettingsAndReEncryptsProtectedFiles()
    {
        File.WriteAllText(Data("shortcuts.json"), """[{"title":"before"}]""");
        JsonStore.SaveProtected(Data("widgets.dat"), new JsonArray(new JsonObject { ["id"] = "w1", ["type"] = "note" }));
        Directory.CreateDirectory(ThemeService.CustomWallpapersFolder);
        File.WriteAllBytes(Path.Combine(ThemeService.CustomWallpapersFolder, "custom-backup.png"), [9, 8, 7]);
        var target = Path.Combine(TestProfile.NewFolder(), "test" + BackupService.Extension);

        var summary = BackupService.Create(target, includeHistory: false, ["uBlock Origin Lite"]);
        Assert.True(summary.Files >= 3);

        using (var zip = ZipFile.OpenRead(target))
        {
            Assert.NotNull(zip.GetEntry("secure/widgets.json")); // decrypted: another Windows account can read it
            Assert.Null(zip.GetEntry("widgets.dat"));
            Assert.Null(zip.GetEntry("secure/history.json")); // history only when asked for
            Assert.NotNull(zip.GetEntry("wallpapers/custom-backup.png"));
        }

        // Things change after the backup...
        File.WriteAllText(Data("shortcuts.json"), """[{"title":"after"}]""");
        File.Delete(Data("widgets.dat"));

        // ...and the restore, applied at the next start, brings them back.
        Assert.NotNull(BackupService.StageRestore(target, out var error));
        Assert.Null(error);
        Assert.True(BackupService.HasPendingRestore);
        BackupService.ApplyPendingRestore();

        Assert.False(BackupService.HasPendingRestore);
        Assert.Contains("before", File.ReadAllText(Data("shortcuts.json")));
        var widgets = JsonStore.LoadProtected<JsonArray>(Data("widgets.dat"));
        Assert.Equal("w1", (string?)widgets?[0]?["id"]);
    }

    [Fact]
    public void UnknownOrEscapingEntriesAreIgnored()
    {
        var folder = TestProfile.NewFolder();
        var source = Path.Combine(folder, "evil" + BackupService.Extension);
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Add(zip, "manifest.json", """{"app":"Sorune","format":1,"version":"0.8.0"}""");
            Add(zip, "../escape.txt", "x");
            Add(zip, "wallpapers/../../escape2.txt", "x");
            Add(zip, "secure/anything.json", "{}");
            Add(zip, "logs/claxe.log", "x");
            Add(zip, "bookmarks.json", """{"items":[]}""");
        }

        Assert.NotNull(BackupService.StageRestore(source, out _));
        var pending = Path.Combine(AppPaths.DataRoot, "pending-restore");
        var staged = Directory.EnumerateFiles(pending, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(pending, f).Replace('\\', '/'))
            .Order()
            .ToList();
        Assert.Equal(["bookmarks.json", "manifest.json"], staged);
        Assert.False(File.Exists(Path.Combine(AppPaths.DataRoot, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(AppPaths.DataRoot)!, "escape.txt")));
        BackupService.CancelRestore();
        Assert.False(BackupService.HasPendingRestore);
    }

    [Fact]
    public void OtherZipsAreRefused()
    {
        var source = Path.Combine(TestProfile.NewFolder(), "other.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            Add(zip, "manifest.json", """{"app":"Something else","format":1}""");

        Assert.Null(BackupService.StageRestore(source, out var error));
        Assert.NotNull(error);
        Assert.False(BackupService.HasPendingRestore);
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
