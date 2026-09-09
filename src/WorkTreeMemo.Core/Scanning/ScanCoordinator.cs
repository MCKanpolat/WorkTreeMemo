using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.Core.Scanning;

public sealed class ScanCoordinator(AppDataStore store, RepoDiscovery discovery, GitCliReader git) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _reindexQueued;
    public event EventHandler<SnapshotDocument>? SnapshotUpdated;
    public event EventHandler<bool>? ScanningChanged;

    public async Task<SnapshotDocument> ScanAsync(bool reindex, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            if (reindex) _reindexQueued = true;
            return await store.LoadSnapshotAsync(ct);
        }

        try
        {
            ScanningChanged?.Invoke(this, true);
            var configuration = await store.LoadConfigurationAsync(ct);
            var existing = await store.LoadSnapshotAsync(ct);
            var repositories = reindex
                ? discovery.Discover(configuration.Roots, configuration.ExcludedDirectoryNames, ct,
                    configuration.ExcludedRepositoryPaths)
                : existing.Repositories.Select(snapshot => snapshot.Repo).ToList();
            var snapshots = new System.Collections.Concurrent.ConcurrentBag<RepoSnapshot>();
            await Parallel.ForEachAsync(repositories,
                new ParallelOptions
                    { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(configuration.MaxParallelism, 1, 8) },
                async (repo, token) =>
                    snapshots.Add(await git.ReadAsync(repo, configuration.BaseBranches, token)));
            var document = new SnapshotDocument(snapshots.OrderBy(snapshot => snapshot.Repo.Name).ToList(),
                DateTimeOffset.UtcNow);
            await store.SaveSnapshotAsync(document, ct);
            SnapshotUpdated?.Invoke(this, document);
            return document;
        }
        finally
        {
            ScanningChanged?.Invoke(this, false);
            _gate.Release();
            if (_reindexQueued)
            {
                _reindexQueued = false;
                _ = Task.Run(() => ScanAsync(true, _shutdown.Token), _shutdown.Token);
            }
        }
    }

    public async Task RunPeriodicAsync(CancellationToken ct)
    {
        var config = await store.LoadConfigurationAsync(ct);
        using var timer = new PeriodicTimer(config.ScanInterval);
        while (await timer.WaitForNextTickAsync(ct)) _ = ScanAsync(false, ct);
    }

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
