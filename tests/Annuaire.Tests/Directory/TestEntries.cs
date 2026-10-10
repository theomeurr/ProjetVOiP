using Annuaire.Web.Directory;

namespace Annuaire.Tests.Directory;

internal static class TestEntries
{
    public static DirectoryEntry Person(
        string id, string surname, string given, string? account = null, string? department = "Urbanisme",
        string? ipPhone = "4210", string? phoneIp = "10.20.1.10", string? mobile = null) => new()
    {
        Id = id,
        Kind = EntryKind.Person,
        AccountName = account,
        DisplayName = $"{given} {surname.ToUpperInvariant()}",
        GivenName = given,
        Surname = surname,
        Department = department,
        IpPhone = ipPhone,
        PhoneIp = phoneIp,
        Mobile = mobile
    };

    public static DirectoryEntry Contact(string id, string name, string? department = "Accueil") => new()
    {
        Id = id,
        Kind = EntryKind.Contact,
        DisplayName = name,
        Department = department,
        IpPhone = "1000"
    };
}
