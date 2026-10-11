using System.Security.Claims;
using Annuaire.Tests.Directory;
using Annuaire.Web.Calling;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling;

public class CallServiceTests
{
    private static readonly DirectoryEntry Laura = TestEntries.Person("u1", "Martin", "Laura", account: "lmartin", ipPhone: "4210", phoneIp: "10.20.1.15");
    private static readonly DirectoryEntry Helene = TestEntries.Person("u2", "Dupont", "Hélène", account: "hdupont", ipPhone: "3099", phoneIp: "10.20.1.30", mobile: "06 12 34 56 78")
        with { TelephoneNumber = "01 34 28 30 99", Department = "Services techniques" };
    private static readonly DirectoryEntry Karim = TestEntries.Person("u3", "Benali", "Karim", account: "kbenali", ipPhone: null, phoneIp: null);
    private static readonly DirectoryEntry Intrus = TestEntries.Person("u4", "Intrus", "Ip", account: "intrus", ipPhone: "5000", phoneIp: "192.168.0.10");
    private static readonly DirectoryEntry SansIp = TestEntries.Person("u5", "Sansip", "Paul", account: "psansip", ipPhone: "5001", phoneIp: null);

    private readonly RecordingProvider _provider = new();
    private readonly ListLoggerFactory _logs = new();

    private CallService Create(ICallProvider? provider = null, DisplayOptions? display = null)
    {
        var directory = new DirectoryService(
            new StaticDirectoryProvider([Laura, Helene, Karim, Intrus, SansIp]),
            new MemoryCache(new MemoryCacheOptions()),
            MsOptions.Create(new DirectoryOptions()),
            TimeProvider.System,
            NullLogger<DirectoryService>.Instance);

        return new CallService(
            directory,
            provider ?? _provider,
            new PhoneNumberNormalizer(MsOptions.Create(new NumberingOptions())),
            new PhoneSubnetPolicy(MsOptions.Create(new CallingOptions { AllowedPhoneSubnets = ["10.20.0.0/16"] })),
            MsOptions.Create(display ?? new DisplayOptions()),
            MsOptions.Create(new CallingOptions()),
            _logs);
    }

    private static ClaimsPrincipal User(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "Test"));

    [Fact]
    public async Task Caller_phone_comes_from_the_directory_never_from_the_request()
    {
        var result = await Create().DialAsync(User(@"MONTMAGNY\lmartin"), "u2", CallField.IpPhone, default);

        Assert.True(result.Success);
        var (caller, number) = Assert.Single(_provider.Calls);
        Assert.Equal("lmartin", caller.AccountName);
        Assert.Equal("4210", caller.Extension);
        Assert.Equal("10.20.1.15", caller.PhoneIp);
        Assert.Equal("3099", number);
        Assert.Contains("Votre poste 4210 appelle Hélène DUPONT", result.Message);
    }

    [Fact]
    public async Task Number_is_read_from_the_target_entry_and_normalized()
    {
        var service = Create();

        await service.DialAsync(User("lmartin"), "u2", CallField.TelephoneNumber, default);
        await service.DialAsync(User("lmartin"), "u2", CallField.Mobile, default);

        Assert.Equal(["3099", "00612345678"], _provider.Calls.Select(c => c.Number));
    }

    [Fact]
    public async Task Hidden_mobile_cannot_be_dialed()
    {
        var service = Create(display: new DisplayOptions { ShowMobile = false });

        var result = await service.DialAsync(User("lmartin"), "u2", CallField.Mobile, default);

        Assert.Equal(CallStatus.NotFound, result.Status);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Unknown_windows_user_dials_manually()
    {
        var service = Create();

        Assert.Equal(CallMode.Manual, service.GetCallerStatus(null).Mode);
        var result = await service.DialAsync(User(@"MONTMAGNY\inconnu"), "u2", CallField.TelephoneNumber, default);

        Assert.Equal(CallStatus.ManualDial, result.Status);
        Assert.Contains("01 34 28 30 99", result.Message);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Caller_without_extension_dials_manually()
    {
        var service = Create();

        var status = service.GetCallerStatus(Karim);
        var result = await service.DialAsync(User("kbenali"), "u2", CallField.IpPhone, default);

        Assert.Equal(CallMode.Manual, status.Mode);
        Assert.Contains("copie le numéro", status.Notice);
        Assert.Equal(CallStatus.ManualDial, result.Status);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Phone_ip_outside_allowed_subnets_falls_back_to_tel_link_without_contacting_the_ip()
    {
        var service = Create();

        Assert.Equal(CallMode.ClientLink, service.GetCallerStatus(Intrus).Mode);
        var result = await service.DialAsync(User("intrus"), "u2", CallField.TelephoneNumber, default);

        Assert.True(result.Success);
        Assert.Equal("tel:3099", result.ClientAction);
        Assert.Empty(_provider.Calls);
        Assert.Contains(_logs.Entries, e => e.Message.Contains("IP du téléphone refusée : 192.168.0.10"));
    }

    [Fact]
    public async Task Missing_phone_ip_falls_back_to_tel_link()
    {
        var result = await Create().DialAsync(User("psansip"), "u2", CallField.Mobile, default);

        Assert.True(result.Success);
        Assert.Equal("tel:00612345678", result.ClientAction);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Calling_yourself_is_refused()
    {
        var result = await Create().DialAsync(User("lmartin"), "u1", CallField.IpPhone, default);

        Assert.Equal(CallStatus.NotAllowed, result.Status);
        Assert.Empty(_provider.Calls);
    }

    [Fact]
    public async Task Unknown_entry_is_reported()
    {
        var result = await Create().DialAsync(User("lmartin"), "nexistepas", CallField.IpPhone, default);

        Assert.Equal(CallStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Tel_link_provider_does_not_need_an_extension()
    {
        var service = Create(new TelLinkProvider(MsOptions.Create(new CallingOptions())));

        var result = await service.DialAsync(User("kbenali"), "u2", CallField.TelephoneNumber, default);

        Assert.True(result.Success);
        Assert.Equal("tel:3099", result.ClientAction);
        Assert.Contains("application de téléphonie", result.Message);
    }

    [Fact]
    public async Task Disabled_provider_means_manual_dialing_for_everyone()
    {
        var service = Create(new DisabledProvider());

        Assert.Equal(CallMode.Manual, service.GetCallerStatus(Laura).Mode);
        var result = await service.DialAsync(User("lmartin"), "u2", CallField.IpPhone, default);
        Assert.Equal(CallStatus.ManualDial, result.Status);
    }

    [Fact]
    public async Task Provider_exception_becomes_a_generic_message_without_details()
    {
        _provider.Throw = true;

        var result = await Create().DialAsync(User("lmartin"), "u2", CallField.IpPhone, default);

        Assert.Equal(CallStatus.ProviderError, result.Status);
        Assert.DoesNotContain("boum", result.Message);
    }

    [Fact]
    public async Task Every_call_is_audited_with_caller_target_number_and_result()
    {
        var service = Create();

        await service.DialAsync(User(@"MONTMAGNY\lmartin"), "u2", CallField.IpPhone, default);
        await service.DialAsync(User("kbenali"), "u2", CallField.IpPhone, default);

        var audit = _logs.Entries.Where(e => e.Category == CallService.AuditLogCategory).ToList();
        Assert.Equal(2, audit.Count);
        Assert.Contains(@"appelant=MONTMAGNY\lmartin", audit[0].Message);
        Assert.Contains("destinataire=Hélène DUPONT", audit[0].Message);
        Assert.Contains("numero=3099", audit[0].Message);
        Assert.Contains("APPEL OK", audit[0].Message);
        Assert.Contains("APPEL ManualDial", audit[1].Message);
        Assert.Equal(LogLevel.Warning, audit[1].Level);
    }

    private sealed class RecordingProvider : ICallProvider
    {
        public List<(CallerInfo Caller, string Number)> Calls { get; } = [];
        public bool Throw { get; set; }
        public string Name => "Test";
        public bool RequiresPhoneIp => true;

        public Task<CallResult> DialAsync(CallerInfo caller, string number)
        {
            if (Throw)
            {
                throw new InvalidOperationException("boum");
            }

            Calls.Add((caller, number));
            return Task.FromResult(CallResult.Ok());
        }
    }

    private sealed class StaticDirectoryProvider(IReadOnlyList<DirectoryEntry> entries) : IDirectoryProvider
    {
        public Task<IReadOnlyList<DirectoryEntry>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(entries);
    }
}

internal sealed class ListLoggerFactory : ILoggerFactory
{
    public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, Entries);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class ListLogger(string category, List<(string, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((category, logLevel, formatter(state, exception)));
    }
}
