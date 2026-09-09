using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.Core.Scanning;

public sealed class ScanCoordinator(AppDataStore store, RepoDiscovery discovery, GitCliReader git) : IAsyncDisposable
{
    // Git status may walk a whole worktree. Keeping one CPU core free prevents foreground UI starvation.
    private const int MaximumScanParallelism = 2;
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
            return await Task.Run(() => store.LoadSnapshotAsync(ct), ct).ConfigureAwait(false);
        }

        try
        {
            ScanningChanged?.Invoke(this, true);
            var document = await Task.Run(async () =>
            {
                var configuration = await store.LoadConfigurationAsync(ct).ConfigureAwait(false);
                var existing = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                var repositories = reindex
                    ? discovery.Discover(configuration.Roots, configuration.ExcludedDirectoryNames, ct,
                        configuration.ExcludedRepositoryPaths)
                    : existing.Repositories.Select(snapshot => snapshot.Repo).ToList();
                var existingByPath = existing.Repositories.ToDictionary(snapshot => snapshot.Repo.Path,
                    StringComparer.OrdinalIgnoreCase);
                var snapshots = new System.Collections.Concurrent.ConcurrentBag<RepoSnapshot>();
                await Parallel.ForEachAsync(repositories,
                    new ParallelOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = Math.Clamp(configuration.MaxParallelism, 1, MaximumScanParallelism)
                    },
                    async (repo, token) =>
                    {
                        var repository = await git.ReadAsync(repo, configuration.BaseBranches, token)
                            .ConfigureAwait(false);
                        existingByPath.TryGetValue(repo.Path, out var previous);
                        var diskSize = reindex || previous?.DiskSizeBytes is null
                            ? DirectorySizeCalculator.GetSize(repo.Path, token)
                            : previous.DiskSizeBytes;
                        snapshots.Add(repository with { DiskSizeBytes = diskSize });
                    }).ConfigureAwait(false);
                var snapshot = new SnapshotDocument(snapshots.OrderBy(snapshot => snapshot.Repo.Name).ToList(),
                    DateTimeOffset.UtcNow);
                await store.SaveSnapshotAsync(snapshot, ct).ConfigureAwait(false);
                if (existing.UpdatedAt != DateTimeOffset.MinValue)
                    foreach (var activity in SnapshotChangeDetector.Detect(existing, snapshot))
                        await store.AppendActivityAsync(activity, ct).ConfigureAwait(false);
                return snapshot;
            }, ct).ConfigureAwait(false);
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
