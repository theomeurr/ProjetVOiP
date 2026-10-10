using Annuaire.Web.Directory;

namespace Annuaire.Tests.Directory;

public class DirectorySnapshotTests
{
    [Fact]
    public void Create_puts_contacts_first_then_sorts_by_surname_ignoring_accents()
    {
        var snapshot = DirectorySnapshot.Create(
        [
            TestEntries.Person("1", "Martin", "Laura"),
            TestEntries.Person("2", "Éluard", "Paul"),
            TestEntries.Contact("3", "Accueil Hôtel de ville"),
            TestEntries.Person("4", "Dubois", "Anne")
        ], DateTimeOffset.UnixEpoch);

        Assert.Equal(["3", "4", "2", "1"], snapshot.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Departments_are_distinct_ignoring_case_and_accents_and_sorted()
    {
        var snapshot = DirectorySnapshot.Create(
        [
            TestEntries.Person("1", "A", "a", department: "Urbanisme"),
            TestEntries.Person("2", "B", "b", department: "urbanisme"),
            TestEntries.Person("3", "C", "c", department: "État civil"),
            TestEntries.Person("4", "D", "d", department: null)
        ], DateTimeOffset.UnixEpoch);

        Assert.Equal(["État civil", "Urbanisme"], snapshot.Departments);
    }

    [Fact]
    public void FindByAccount_is_case_insensitive_and_ignores_contacts()
    {
        var snapshot = DirectorySnapshot.Create(
        [
            TestEntries.Person("1", "Martin", "Laura", account: "lmartin"),
            TestEntries.Contact("2", "Accueil")
        ], DateTimeOffset.UnixEpoch);

        Assert.Equal("1", snapshot.FindByAccount("LMARTIN")?.Id);
        Assert.Null(snapshot.FindByAccount("inconnu"));
    }
}
