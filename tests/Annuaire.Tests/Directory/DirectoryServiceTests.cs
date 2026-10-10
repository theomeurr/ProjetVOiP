using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Directory;

public class DirectoryServiceTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T08:00:00Z"));

    private DirectoryService CreateService(FakeProvider provider, int cacheMinutes = 15)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { Clock = new SystemClockAdapter(_time) });
        return new DirectoryService(provider, cache, MsOptions.Create(new DirectoryOptions { CacheMinutes = cacheMinutes }),
            _time, NullLogger<DirectoryService>.Instance);
    }

    [Fact]
    public async Task Snapshot_is_cached_for_the_configured_duration()
    {
        var provider = new FakeProvider();
        var service = CreateService(provider, cacheMinutes: 15);

        await service.GetSnapshotAsync();
        _time.Advance(TimeSpan.FromMinutes(14));
        await service.GetSnapshotAsync();
        Assert.Equal(1, provider.Calls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await service.GetSnapshotAsync();
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Last_good_snapshot_is_served_when_source_fails()
    {
        var provider = new FakeProvider();
        var service = CreateService(provider);
        var first = await service.GetSnapshotAsync();

        provider.Fail = true;
        _time.Advance(TimeSpan.FromMinutes(16));
        var second = await service.GetSnapshotAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Initial_failure_throws_directory_unavailable()
    {
        var service = CreateService(new FakeProvider { Fail = true });
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.GetSnapshotAsync());
    }

    [Fact]
    public async Task FindCurrentUser_resolves_windows_identity()
    {
        var service = CreateService(new FakeProvider());
        var user = await service.FindCurrentUserAsync(@"MONTMAGNY\LMartin");
        Assert.Equal("1", user?.Id);
    }

    private sealed class FakeProvider : IDirectoryProvider
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<IReadOnlyList<DirectoryEntry>> LoadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("AD injoignable");
            }

            return Task.FromResult<IReadOnlyList<DirectoryEntry>>([TestEntries.Person("1", "Martin", "Laura", account: "lmartin")]);
        }
    }

    private sealed class SystemClockAdapter(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
