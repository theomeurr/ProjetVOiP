using System.Globalization;
using System.Text;

namespace Annuaire.Web.Directory;

/// <summary>Normalisation pour la recherche : minuscules, sans accents, espaces compactés.</summary>
public static class TextNormalizer
{
    public static string ForSearch(params string?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendNormalized(builder, value);
        }

        return builder.ToString();
    }

    private static void AppendNormalized(StringBuilder builder, string value)
    {
        // Démarre « après un espace » pour ignorer les espaces et la ponctuation de tête.
        var lastWasSpace = true;
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || c is '-' or '–' or '—' or '\'' or '’' or '.' or ',' or '/' or '(' or ')')
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(c switch
            {
                'œ' or 'Œ' => "oe",
                'æ' or 'Æ' => "ae",
                'ß' => "ss",
                _ => char.ToLowerInvariant(c).ToString()
            });
            lastWasSpace = false;
        }

        // Supprime un éventuel espace final introduit par la ponctuation.
        while (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }
    }
}
