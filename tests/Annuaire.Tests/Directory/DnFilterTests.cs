using Annuaire.Web.Directory;

namespace Annuaire.Tests.Directory;

public class DnFilterTests
{
    private static readonly string[] Excluded = ["OU=Comptes techniques,DC=montmagny,DC=local"];

    [Theory]
    [InlineData("CN=svc-backup,OU=Comptes techniques,DC=montmagny,DC=local", true)]
    [InlineData("CN=x,OU=Sous-OU,OU=Comptes techniques,DC=montmagny,DC=local", true)]
    [InlineData("cn=x, ou=comptes techniques, dc=montmagny, dc=local", true)]
    [InlineData("CN=Laura Martin,OU=Agents,DC=montmagny,DC=local", false)]
    [InlineData("CN=x,OU=Autres Comptes techniques,DC=montmagny,DC=local", false)]
    public void IsExcluded_matches_ou_and_sub_ous(string dn, bool expected)
    {
        Assert.Equal(expected, DnFilter.IsExcluded(dn, Excluded));
    }
}
