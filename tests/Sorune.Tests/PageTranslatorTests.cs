using System.Text.Json;
using Claxe.Services;

namespace Sorune.Tests;

public class PageTranslatorTests
{
    [Fact]
    public void ReadsBothAnswerShapes()
    {
        using var pairs = JsonDocument.Parse("""[["Selam Dünya","en"],["Ben &amp; sen","en"],42]""");
        var result = PageTranslator.Parse(pairs.RootElement);
        Assert.Equal(3, result.Count);
        Assert.Equal(("Selam Dünya", "en"), result[0]);
        Assert.Equal("Ben & sen", result[1].Text); // HTML entities decoded
        Assert.Equal("", result[2].Text); // unknown entries keep their place

        using var plain = JsonDocument.Parse("""["Günaydın"]""");
        Assert.Equal(("Günaydın", null), PageTranslator.Parse(plain.RootElement)[0]);
    }

    [Fact]
    public void LanguageNamesFallBackToTheCode()
        => Assert.Equal("XX", PageTranslator.LanguageName("xx"));
}
