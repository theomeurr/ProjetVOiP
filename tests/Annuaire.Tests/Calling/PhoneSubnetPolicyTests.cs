using Annuaire.Web.Calling;
using Annuaire.Web.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling;

public class PhoneSubnetPolicyTests
{
    private static PhoneSubnetPolicy Create(params string[] subnets) =>
        new(MsOptions.Create(new CallingOptions { AllowedPhoneSubnets = [.. subnets] }));

    [Theory]
    [InlineData("10.20.1.15", true)]
    [InlineData("10.20.255.1", true)]
    [InlineData("10.21.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.20.1.15:8080", false)]
    [InlineData("telephone.montmagny.local", false)]
    [InlineData("10.20.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_canonical_ips_inside_allowed_subnets_are_accepted(string? ip, bool allowed)
    {
        Assert.Equal(allowed, Create("10.20.0.0/16").IsAllowed(ip));
    }

    [Fact]
    public void Without_restriction_any_valid_ip_except_loopback_is_accepted()
    {
        var policy = Create();
        Assert.False(policy.HasRestriction);
        Assert.True(policy.IsAllowed("192.168.1.10"));
        Assert.False(policy.IsAllowed("127.0.0.1"));
        Assert.False(policy.IsAllowed("pas-une-ip"));
    }

    [Fact]
    public void Invalid_subnet_configuration_fails_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create("10.20.0.0/99"));
        Assert.Contains("AllowedPhoneSubnets", ex.Message);
    }
}
