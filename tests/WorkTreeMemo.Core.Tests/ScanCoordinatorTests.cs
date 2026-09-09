using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.Core.Tests;

public sealed class ScanCoordinatorTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reindex_persists_empty_snapshot_when_no_scan_roots_are_configured()
    {
        var store = new AppDataStore(_path);
        await store.SaveConfigurationAsync(AppConfiguration.Default with { Roots = [] });
        await using var coordinator = new ScanCoordinator(store, new RepoDiscovery(), new GitCliReader());

        var actual = await coordinator.ScanAsync(true);

        Assert.Empty(actual.Repositories);
        var persisted = await store.LoadSnapshotAsync();
        Assert.Empty(persisted.Repositories);
        Assert.NotEqual(DateTimeOffset.MinValue, persisted.UpdatedAt);
    }

    [Fact]
    public async Task Reindex_raises_its_completed_snapshot_once()
    {
        var store = new AppDataStore(_path);
        await store.SaveConfigurationAsync(AppConfiguration.Default with { Roots = [] });
        await using var coordinator = new ScanCoordinator(store, new RepoDiscovery(), new GitCliReader());
        var updates = new List<SnapshotDocument>();
        coordinator.SnapshotUpdated += (_, snapshot) => updates.Add(snapshot);

        var actual = await coordinator.ScanAsync(true);

        var update = Assert.Single(updates);
        Assert.Same(actual, update);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
        return ValueTask.CompletedTask;
    }
}
