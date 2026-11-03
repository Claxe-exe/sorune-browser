using Claxe.Services;

namespace Sorune.Tests;

public class TabAudioTests
{
    [Theory]
    [InlineData(250, "(2.5)")]
    [InlineData(100, "(1)")]
    [InlineData(50, "(0.5)")]
    [InlineData(999, "(3)")]
    [InlineData(-5, "(0)")]
    public void VolumeIsAFactorWithinTheLimit(int percent, string expected)
        => Assert.EndsWith(expected + ";", TabAudio.SetCall(percent));

    [Fact]
    public void TheScriptHasNoPlaceholdersLeft()
    {
        var script = TabAudio.Script(150);

        Assert.DoesNotContain("__VALUE__", script);
        Assert.DoesNotContain("__MAX__", script);
        Assert.Contains("const initial = 1.5;", script);
    }
}
