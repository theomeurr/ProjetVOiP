namespace Annuaire.Web.Directory;

public static class AccountName
{
    /// <summary>
    /// Extrait le sAMAccountName d'une identité Windows :
    /// "MONTMAGNY\jdupont" ou "jdupont@montmagny.local" → "jdupont".
    /// </summary>
    public static string? FromIdentity(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
        {
            return null;
        }

        var name = identityName.Trim();
        var backslash = name.LastIndexOf('\\');
        if (backslash >= 0)
        {
            name = name[(backslash + 1)..];
        }

        var at = name.IndexOf('@');
        if (at >= 0)
        {
            name = name[..at];
        }

        return name.Length == 0 ? null : name;
    }
}
