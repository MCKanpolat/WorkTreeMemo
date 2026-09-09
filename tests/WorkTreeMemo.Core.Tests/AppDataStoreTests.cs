using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.Core.Tests;

public sealed class AppDataStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Configuration_round_trips_through_atomic_json_file()
    {
        var store = new AppDataStore(_path);
        var expected = AppConfiguration.Default with
        {
            Culture = "en-US", Roots = [new ScanRoot("/tmp/repos")], FavoriteRepositoryPaths = ["/tmp/repos/favorite"],
            LastWindowPlacement = new WindowPlacement(120, 80, 1360, 860)
        };
        await store.SaveConfigurationAsync(expected);
        var actual = await store.LoadConfigurationAsync();
        Assert.Equal(expected.Culture, actual.Culture);
        Assert.Single(actual.Roots);
        Assert.Equal(expected.FavoriteRepositoryPaths, actual.FavoriteRepositoryPaths);
        Assert.Equal(expected.LastWindowPlacement, actual.LastWindowPlacement);
    }

    [Fact]
    public void Default_configuration_marks_unmerged_branches_stale_after_fourteen_days()
    {
        Assert.Equal(14, AppConfiguration.Default.StaleAfterDays);
        Assert.Equal(14, AppConfiguration.Default.IgnoreBranchesOlderThanDays);
    }

    [Fact]
    public async Task Concurrent_configuration_loads_do_not_share_a_temporary_file()
    {
        var store = new AppDataStore(_path);

        var configurations = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => store.LoadConfigurationAsync()));

        Assert.All(configurations, configuration =>
        {
            Assert.Equal(AppConfiguration.Default.StaleAfterDays, configuration.StaleAfterDays);
            Assert.Equal(AppConfiguration.Default.IgnoreBranchesOlderThanDays, configuration.IgnoreBranchesOlderThanDays);
        });
        Assert.True(File.Exists(Path.Combine(_path, "config.json")));
    }

    [Fact]
    public async Task Snapshot_notes_and_activity_round_trip()
    {
        var store = new AppDataStore(_path);
        var stashDate = DateTimeOffset.UtcNow;
        var snapshot = new SnapshotDocument(
            [new RepoSnapshot(new RepoRef("/repo", "repo"), [], [new WorktreeState("/repo", "main", 0, 0, 0, 1, stashDate)], stashDate)],
            stashDate);
        var notes = new Dictionary<string, Note> { ["/repo\nmain"] = new("hold", true, DateTimeOffset.UtcNow, true) };

        await store.SaveSnapshotAsync(snapshot);
        await store.SaveNotesAsync(notes);
        await store.AppendActivityAsync(new ActivityEntry(DateTimeOffset.UtcNow, "/repo", "Detected change"));

        Assert.Single((await store.LoadSnapshotAsync()).Repositories);
        Assert.Equal(stashDate, (await store.LoadSnapshotAsync()).Repositories[0].Worktrees[0].LatestStashAt);
        Assert.Equal("hold", (await store.LoadNotesAsync())["/repo\nmain"].Text);
        Assert.True((await store.LoadNotesAsync())["/repo\nmain"].IsFlagged);
        Assert.True(File.Exists(Path.Combine(_path, "activity.jsonl")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
