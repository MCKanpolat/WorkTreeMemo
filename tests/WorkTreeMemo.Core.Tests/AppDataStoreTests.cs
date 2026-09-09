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
        var expected = AppConfiguration.Default with { Culture = "en-US", Roots = [new ScanRoot("/tmp/repos")] };
        await store.SaveConfigurationAsync(expected);
        var actual = await store.LoadConfigurationAsync();
        Assert.Equal(expected.Culture, actual.Culture);
        Assert.Single(actual.Roots);
    }

    [Fact]
    public async Task Snapshot_notes_and_activity_round_trip()
    {
        var store = new AppDataStore(_path);
        var snapshot = new SnapshotDocument([new RepoSnapshot(new RepoRef("/repo", "repo"), [], [], DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow);
        var notes = new Dictionary<string, Note> { ["/repo\nmain"] = new("hold", true, DateTimeOffset.UtcNow) };

        await store.SaveSnapshotAsync(snapshot);
        await store.SaveNotesAsync(notes);
        await store.AppendActivityAsync(new ActivityEntry(DateTimeOffset.UtcNow, "/repo", "Detected change"));

        Assert.Single((await store.LoadSnapshotAsync()).Repositories);
        Assert.Equal("hold", (await store.LoadNotesAsync())["/repo\nmain"].Text);
        Assert.True(File.Exists(Path.Combine(_path, "activity.jsonl")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
