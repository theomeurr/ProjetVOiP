using Annuaire.Web.Directory;

namespace Annuaire.Tests.Directory;

public class AccountNameTests
{
    [Theory]
    [InlineData(@"MONTMAGNY\jdupont", "jdupont")]
    [InlineData("jdupont@montmagny.local", "jdupont")]
    [InlineData("jdupont", "jdupont")]
    [InlineData(" MONTMAGNY\\jdupont ", "jdupont")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(@"MONTMAGNY\", null)]
    public void FromIdentity_extracts_sam_account_name(string? identity, string? expected)
    {
        Assert.Equal(expected, AccountName.FromIdentity(identity));
    }
}
