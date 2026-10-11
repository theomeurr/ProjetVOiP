using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling;

/// <param name="Dialable">Chiffres à composer (préfixe de sortie inclus pour un numéro externe).</param>
/// <param name="IsInternal">Vrai pour un poste interne (y compris une SDA de la mairie convertie en poste).</param>
public sealed record NormalizedNumber(string Dialable, bool IsInternal);

/// <summary>
/// Normalise un numéro de l'annuaire avant composition :
/// nettoyage (espaces, points, tirets, parenthèses, +33), postes internes tels quels,
/// SDA de la mairie converties en poste interne, préfixe de sortie pour les numéros externes.
/// </summary>
public sealed class PhoneNumberNormalizer(IOptions<NumberingOptions> options)
{
    private const int MaxDigits = 20;

    public bool TryNormalize(string? raw, out NormalizedNumber result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var config = options.Value;
        var cleaned = Clean(raw);
        if (cleaned.Length == 0)
        {
            return false;
        }

        string national;
        if (cleaned[0] == '+')
        {
            var digits = cleaned[1..];
            if (!IsDigits(digits))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(config.CountryCode) && digits.StartsWith(config.CountryCode, StringComparison.Ordinal))
            {
                national = "0" + digits[config.CountryCode.Length..];
            }
            else
            {
                return External(config.InternationalPrefix + digits, config, out result);
            }
        }
        else if (!IsDigits(cleaned))
        {
            return false;
        }
        else if (!string.IsNullOrEmpty(config.CountryCode)
                 && !string.IsNullOrEmpty(config.InternationalPrefix)
                 && cleaned.StartsWith(config.InternationalPrefix + config.CountryCode, StringComparison.Ordinal))
        {
            national = "0" + cleaned[(config.InternationalPrefix.Length + config.CountryCode.Length)..];
        }
        else
        {
            national = cleaned;
        }

        if (national.Length > MaxDigits)
        {
            return false;
        }

        // Poste interne : composé tel quel.
        if (national.Length == config.InternalExtensionLength)
        {
            result = new NormalizedNumber(national, IsInternal: true);
            return true;
        }

        // SDA de la mairie : on compose directement le poste, sans sortir sur le réseau public.
        foreach (var prefix in config.InternalSdaPrefixes.Select(Clean).Where(p => p.Length > 0))
        {
            if (national.Length == prefix.Length + config.InternalExtensionLength
                && national.StartsWith(prefix, StringComparison.Ordinal))
            {
                result = new NormalizedNumber(national[^config.InternalExtensionLength..], IsInternal: true);
                return true;
            }
        }

        return External(national, config, out result);
    }

    private static bool External(string number, NumberingOptions config, out NormalizedNumber result)
    {
        var dialable = (config.OutsideLinePrefix ?? "") + number;
        result = new NormalizedNumber(dialable, IsInternal: false);
        return dialable.Length <= MaxDigits + 4;
    }

    /// <summary>Retire espaces (y compris insécables), points, tirets, barres, parenthèses et le « (0) » après +33.</summary>
    private static string Clean(string raw)
    {
        var value = raw.Trim().Replace("(0)", "", StringComparison.Ordinal);
        var chars = value.Where(c => !(char.IsWhiteSpace(c) || c is '.' or '-' or '/' or '(' or ')' or ' ' or ' '));
        return new string(chars.ToArray());
    }

    private static bool IsDigits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);
}
