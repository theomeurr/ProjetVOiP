using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Annuaire.Web.Calling;

/// <summary>Remplace les variables {callerExt}, {callerIp}, {number} dans un modèle configuré.</summary>
public static partial class TemplateRenderer
{
    public static IReadOnlyDictionary<string, string> Variables(CallerInfo caller, string number) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["callerExt"] = caller.Extension ?? "",
            ["callerIp"] = caller.PhoneIp ?? "",
            ["number"] = number
        };

    /// <summary>Remplace les variables connues ; chaque valeur passe par <paramref name="encode"/>.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> variables, Func<string, string> encode) =>
        VariablePattern().Replace(template, match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? encode(value) : match.Value);

    public static bool UsesVariable(string? template, string name) =>
        !string.IsNullOrEmpty(template) && template.Contains("{" + name + "}", StringComparison.OrdinalIgnoreCase);

    public static string UrlEncode(string value) => Uri.EscapeDataString(value);

    /// <summary>Échappe pour une chaîne JSON (sans les guillemets englobants).</summary>
    public static string JsonEncode(string value) => JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(value);

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex VariablePattern();
}
