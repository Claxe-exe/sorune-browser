using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Claxe.Models;
using Claxe.Services;
using Claxe.ViewModels;

namespace Sorune.Tests;

public class ColorExtractorTests
{
    private static byte[] Pixels(params (byte R, byte G, byte B, int Count)[] groups)
        => groups.SelectMany(g => Enumerable.Repeat(new[] { g.B, g.G, g.R, (byte)255 }, g.Count).SelectMany(p => p)).ToArray();

    [Fact]
    public void MostCommonVividColourComesFirst()
    {
        var colors = ColorExtractor.FromPixels(Pixels((220, 30, 40, 300), (30, 60, 220, 100), (128, 128, 128, 500)), 3);
        Assert.Equal(2, colors.Count); // grey is not vivid
        Assert.StartsWith("#", colors[0]);
        Assert.True(Convert.ToInt32(colors[0][1..3], 16) > 180); // red
        Assert.True(Convert.ToInt32(colors[1][5..7], 16) > 180); // blue
    }

    [Fact]
    public void DarkOrGreyImagesGiveNothing()
        => Assert.Empty(ColorExtractor.FromPixels(Pixels((10, 10, 12, 100), (100, 100, 100, 100)), 3));
}

public class WidgetStoreTests
{
    [Fact]
    public void KeepsOnlyKnownTypesAndFieldsWithLimits()
    {
        var store = new WidgetStore();
        var input = JsonSerializer.SerializeToElement(new object[]
        {
            new { id = "n1", type = "note", text = new string('x', 6000), evil = "<script>" },
            new { id = "bad id!", type = "note", text = "dropped" },
            new { id = "u1", type = "unknown" },
            new { id = "t1", type = "timer", focus = 500, rest = -3 },
        });

        var saved = store.Save(input);
        var types = saved.Select(w => (string?)w!["type"]).ToList();
        Assert.Equal(["clock", "note", "timer"], types); // the big clock is always there
        var note = saved[1]!;
        Assert.Equal(5000, ((string)note["text"]!).Length);
        Assert.Null(note["evil"]);
        Assert.Equal(120, (int)saved[2]!["focus"]!);
        Assert.Equal(1, (int)saved[2]!["rest"]!);
    }

    [Fact]
    public void NotAnArrayFallsBackToDefaults()
    {
        var saved = new WidgetStore().Save(JsonSerializer.SerializeToElement(new { hello = 1 }));
        Assert.Contains(saved, w => (string?)w!["type"] == "clock");
    }
}

public class SettingsValidationTests
{
    private static SettingsController Controller(AppSettings settings)
        => new(settings, new PermissionStore(), new PanelStore());

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Theory]
    [InlineData("density", "huge")]
    [InlineData("sleepAfterMinutes", 7)]
    [InlineData("newTabBlur", 31)]
    [InlineData("mascotPeek", 0.95)]
    [InlineData("soundPack", "dubstep")]
    [InlineData("newTabEffect", "fireworks")]
    [InlineData("nope", true)]
    public void RejectsInvalidValues(string key, object value)
        => Assert.False(Controller(new AppSettings()).TryUpdate(key, Json(value)));

    [Fact]
    public void AcceptsValidValues()
    {
        var settings = new AppSettings();
        var controller = Controller(settings);
        Assert.True(controller.TryUpdate("sleepAfterMinutes", Json(30)));
        Assert.True(controller.TryUpdate("soundPack", Json("retro")));
        Assert.True(controller.TryUpdate("newTabDim", Json(0.6)));
        Assert.True(controller.TryUpdate("newTabAlign", Json("center")));
        Assert.Equal(30, settings.SleepAfterMinutes);
        Assert.Equal("retro", settings.SoundPack);
        Assert.Equal(0.6, settings.NewTabDim);
        Assert.Equal("center", settings.NewTabAlign);
    }
}

public class AddressResolverTests
{
    private static readonly SearchEngine Engine = SearchEngines.Default;

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://github.com/Claxe-exe", "https://github.com/Claxe-exe")]
    public void AddressesNavigate(string input, string expected)
        => Assert.Equal(expected, AddressResolver.Resolve(input, Engine));

    [Fact]
    public void WordsAreSearched()
        => Assert.StartsWith(Engine.BuildUrl(""), AddressResolver.Resolve("anime wallpaper", Engine));

    [Fact]
    public void InternalNamesResolve()
        => Assert.Equal(InternalPages.SettingsUrl, AddressResolver.Resolve("sorune://ayarlar", Engine));
}

public class ExtensionPackageTests
{
    private static byte[] ZipWithManifest()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("ext/manifest.json").Open());
            writer.Write("{\"manifest_version\":3,\"name\":\"Test\",\"version\":\"1.0\"}");
        }
        return stream.ToArray();
    }

    [Fact]
    public void ExtractsZip()
    {
        var folder = TestProfile.NewFolder();
        var file = Path.Combine(folder, "ext.zip");
        File.WriteAllBytes(file, ZipWithManifest());
        var target = Path.Combine(folder, "out");
        ExtensionService.Extract(file, target);
        Assert.True(File.Exists(Path.Combine(target, "ext", "manifest.json")));
    }

    [Fact]
    public void ExtractsCrx3BySkippingItsHeader()
    {
        var folder = TestProfile.NewFolder();
        var header = new byte[] { 1, 2, 3, 4, 5 };
        var crx = "Cr24"u8.ToArray()
            .Concat(BitConverter.GetBytes(3u))
            .Concat(BitConverter.GetBytes((uint)header.Length))
            .Concat(header)
            .Concat(ZipWithManifest())
            .ToArray();
        var file = Path.Combine(folder, "ext.crx");
        File.WriteAllBytes(file, crx);
        var target = Path.Combine(folder, "out");
        ExtensionService.Extract(file, target);
        Assert.True(File.Exists(Path.Combine(target, "ext", "manifest.json")));
    }
}
