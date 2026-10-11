using Annuaire.Web.Calling;
using Annuaire.Web.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling;

public class PhoneNumberNormalizerTests
{
    private static PhoneNumberNormalizer Create(Action<NumberingOptions>? configure = null)
    {
        var options = new NumberingOptions();
        configure?.Invoke(options);
        return new PhoneNumberNormalizer(MsOptions.Create(options));
    }

    [Theory]
    // Postes internes : composés tels quels.
    [InlineData("4210", "4210", true)]
    [InlineData(" 42 10 ", "4210", true)]
    // SDA de la mairie (01 34 28 XX XX) : convertie en poste interne.
    [InlineData("01 34 28 42 10", "4210", true)]
    [InlineData("01.34.28.42.10", "4210", true)]
    [InlineData("+33 1 34 28 42 10", "4210", true)]
    [InlineData("+33 (0)1 34 28 42 10", "4210", true)]
    [InlineData("0033134284210", "4210", true)]
    // Numéros externes : préfixe de sortie « 0 ».
    [InlineData("01 39 83 00 00", "00139830000", false)]
    [InlineData("06 12 34 56 78", "00612345678", false)]
    [InlineData("06-12-34-56-78", "00612345678", false)]
    [InlineData("+33 6 12 34 56 78", "00612345678", false)]
    [InlineData("+33 6 12 34 56 78", "00612345678", false)]
    // International hors France : préfixe international puis préfixe de sortie.
    [InlineData("+44 20 7946 0000", "000442079460000", false)]
    // Numéros courts (urgence, services) : sortie sur le réseau public.
    [InlineData("112", "0112", false)]
    public void Normalizes_numbers(string raw, string expected, bool isInternal)
    {
        Assert.True(Create().TryNormalize(raw, out var result));
        Assert.Equal(expected, result.Dialable);
        Assert.Equal(isInternal, result.IsInternal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("poste 4210")]
    [InlineData("4210;5000")]
    [InlineData("12+34")]
    [InlineData("+")]
    [InlineData("0123456789012345678901234")]
    public void Rejects_invalid_numbers(string? raw)
    {
        Assert.False(Create().TryNormalize(raw, out _));
    }

    [Fact]
    public void Outside_prefix_is_configurable()
    {
        var normalizer = Create(o => o.OutsideLinePrefix = "");
        Assert.True(normalizer.TryNormalize("06 12 34 56 78", out var result));
        Assert.Equal("0612345678", result.Dialable);
    }

    [Fact]
    public void Sda_conversion_can_be_disabled()
    {
        var normalizer = Create(o => o.InternalSdaPrefixes = []);
        Assert.True(normalizer.TryNormalize("01 34 28 42 10", out var result));
        Assert.Equal("00134284210", result.Dialable);
        Assert.False(result.IsInternal);
    }

    [Fact]
    public void Internal_length_is_configurable()
    {
        var normalizer = Create(o =>
        {
            o.InternalExtensionLength = 3;
            o.InternalSdaPrefixes = ["0134284"];
        });

        Assert.True(normalizer.TryNormalize("421", out var internalExt));
        Assert.True(internalExt.IsInternal);
        Assert.True(normalizer.TryNormalize("01 34 28 42 10", out var sda));
        Assert.Equal("210", sda.Dialable);
        Assert.True(normalizer.TryNormalize("4210", out var fourDigits));
        Assert.False(fourDigits.IsInternal);
    }
}
