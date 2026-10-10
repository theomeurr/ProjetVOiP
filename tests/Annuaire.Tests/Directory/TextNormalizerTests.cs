using Annuaire.Web.Directory;

namespace Annuaire.Tests.Directory;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Hélène", "helene")]
    [InlineData("ÉTAT CIVIL – Élections", "etat civil elections")]
    [InlineData("Aïcha N'Diaye", "aicha n diaye")]
    [InlineData("Jean-François", "jean francois")]
    [InlineData("Cœur", "coeur")]
    [InlineData("  Espaces   multiples ", "espaces multiples")]
    public void ForSearch_removes_accents_and_case(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ForSearch(input));
    }

    [Fact]
    public void ForSearch_joins_values_and_skips_empty_ones()
    {
        Assert.Equal("laura martin 4210", TextNormalizer.ForSearch("Laura", null, " ", "MARTIN", "4210"));
    }
}
