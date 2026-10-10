using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Directory;

public sealed class MockDirectoryProviderTests : IDisposable
{
    private readonly string _root = System.IO.Directory.CreateTempSubdirectory("annuaire-tests-").FullName;

    [Fact]
    public async Task Load_excludes_disabled_accounts_and_reads_photos()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "photo.jpg"), [0xFF, 0xD8, 0xFF]);
        await File.WriteAllTextAsync(Path.Combine(_root, "secret.txt"), "ne doit pas être lu");
        await File.WriteAllTextAsync(Path.Combine(_root, "mock.json"), """
        {
          "entries": [
            { "id": "1", "kind": "Person", "accountName": "lmartin", "displayName": "Laura MARTIN", "photoFile": "photo.jpg" },
            { "id": "2", "kind": "Person", "displayName": "Ancien AGENT", "enabled": false },
            { "id": "3", "kind": "Contact", "displayName": "Accueil", "photoFile": "../outside.jpg" }
          ]
        }
        """);

        var entries = await CreateProvider("mock.json").LoadAsync(CancellationToken.None);

        Assert.Equal(["1", "3"], entries.Select(e => e.Id));
        Assert.True(entries[0].HasPhoto);
        Assert.Equal(EntryKind.Contact, entries[1].Kind);
        Assert.False(entries[1].HasPhoto);
    }

    [Fact]
    public async Task Missing_file_throws_directory_unavailable()
    {
        await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => CreateProvider("absent.json").LoadAsync(CancellationToken.None));
    }

    private MockDirectoryProvider CreateProvider(string file) => new(
        MsOptions.Create(new DirectoryOptions { MockFile = file }),
        new FakeEnvironment(_root),
        NullLogger<MockDirectoryProvider>.Instance);

    public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
    }
}
