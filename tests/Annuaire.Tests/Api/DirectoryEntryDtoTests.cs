using System.Text.Json;
using Annuaire.Tests.Directory;
using Annuaire.Web.Api;
using Annuaire.Web.Options;

namespace Annuaire.Tests.Api;

public class DirectoryEntryDtoTests
{
    [Fact]
    public void Dto_never_exposes_account_name_phone_ip_or_photo_bytes()
    {
        var entry = TestEntries.Person("1", "Martin", "Laura", account: "lmartin", phoneIp: "10.20.1.10")
            with { Photo = [1, 2, 3] };

        var json = JsonSerializer.Serialize(DirectoryEntryDto.From(entry, new DisplayOptions()));

        Assert.DoesNotContain("lmartin", json);
        Assert.DoesNotContain("10.20.1.10", json);
        Assert.Contains("\"HasPhoto\":true", json);
    }

    [Fact]
    public void Search_text_contains_normalized_names_and_digits_only_numbers()
    {
        var entry = TestEntries.Person("1", "Lefèbvre", "Hélène", department: "État civil") with
        {
            TelephoneNumber = "01 34 28 42 10"
        };

        var dto = DirectoryEntryDto.From(entry, new DisplayOptions());

        Assert.Contains("helene", dto.Search);
        Assert.Contains("lefebvre", dto.Search);
        Assert.Contains("etat civil", dto.Search);
        Assert.Contains("0134284210", dto.Search);
    }

    [Theory]
    [InlineData(true, new string[0], "Urbanisme", true)]
    [InlineData(false, new string[0], "Urbanisme", false)]
    [InlineData(true, new[] { "Police municipale" }, "Urbanisme", false)]
    [InlineData(true, new[] { "Police municipale" }, "police municipale", true)]
    public void Mobile_visibility_follows_display_options(bool showMobile, string[] departments, string department, bool visible)
    {
        var entry = TestEntries.Person("1", "Martin", "Laura", department: department, mobile: "06 12 34 56 78");
        var options = new DisplayOptions { ShowMobile = showMobile, MobileDepartments = [.. departments] };

        var dto = DirectoryEntryDto.From(entry, options);

        Assert.Equal(visible, dto.Mobile is not null);
    }
}
