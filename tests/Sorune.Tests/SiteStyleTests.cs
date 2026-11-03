using Claxe.Services;

namespace Sorune.Tests;

public class SiteStyleTests
{
    [Theory]
    [InlineData("https://www.YouTube.com/watch?v=1", "youtube.com")]
    [InlineData("http://m.youtube.com/", "m.youtube.com")]
    [InlineData("sorune://ayarlar", null)]
    [InlineData("file:///C:/a.html", null)]
    [InlineData("not a url", null)]
    public void HostIsTheKeyWithoutWww(string url, string? expected)
        => Assert.Equal(expected, SiteStyleStore.HostOf(url));

    [Theory]
    [InlineData("#masthead > div.a:nth-of-type(2)", true)]
    [InlineData("a}body{display:none", false)]
    [InlineData("a;b", false)]
    [InlineData("@import x", false)]
    [InlineData("a[style*=\"url(https://evil\"]", false)]
    [InlineData("  ", false)]
    public void SelectorsCannotBreakOutOfTheirRule(string selector, bool valid)
        => Assert.Equal(valid, SiteStyleStore.IsValidSelector(selector));

    [Fact]
    public void AnOffOrMissingStyleBuildsNothing()
    {
        Assert.Equal("", SiteStyles.BuildCss(null, "#B583FF"));
        Assert.Equal("", SiteStyles.BuildCss(new SiteStyle { Hue = 40, Enabled = false }, "#B583FF"));
    }

    [Fact]
    public void CssCarriesEveryChoice()
    {
        var css = SiteStyles.BuildCss(new SiteStyle
        {
            Hue = 40,
            Font = "Georgia",
            ThemeLinks = true,
            Hidden = ["#ads"],
            Css = "p{margin:0}",
        }, "#B583FF");

        Assert.Contains("hue-rotate(40deg)", css);
        Assert.Contains("hue-rotate(-40deg)", css); // pictures keep their colours
        Assert.Contains("\"Georgia\"", css);
        Assert.Contains("color:#B583FF", css);
        Assert.Contains("#ads{display:none!important}", css);
        Assert.EndsWith("p{margin:0}", css);
    }

    [Fact]
    public void ThePageCannotEscapeTheScriptThroughItsStyle()
    {
        var script = SiteStyles.DocumentScript([new SiteStyle { Host = "a.com", Css = "</script>\"};alert(1)//" }], "#B583FF");

        Assert.DoesNotContain("</script>", script);
        Assert.DoesNotContain("\"};alert", script);
    }

    [Theory]
    [InlineData("#ff0000", "#00ff00", 120)]
    [InlineData("#0000ff", "#ff0000", 120)]
    [InlineData("#ff0000", "#0000ff", -120)]
    [InlineData("#ff0000", "#ff0000", 0)]
    public void HueTurnsTheShortWay(string from, string to, int expected)
        => Assert.Equal(expected, SiteStyles.HueTurn(from, to));
}
