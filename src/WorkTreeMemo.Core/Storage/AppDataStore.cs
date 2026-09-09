using System.Text.Json;
using System.Text.Json.Serialization;
using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Storage;

public sealed class AppDataStore(string? baseDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DirectoryPath { get; } = baseDirectory ??
                                           Path.Combine(
                                               Environment.GetFolderPath(Environment.SpecialFolder
                                                   .LocalApplicationData), "WorkTreeMemo");

    private string ConfigPath => Path.Combine(DirectoryPath, "config.json");
    private string SnapshotPath => Path.Combine(DirectoryPath, "snapshot.json");
    private string NotesPath => Path.Combine(DirectoryPath, "notes.json");
    private string ActivityPath => Path.Combine(DirectoryPath, "activity.jsonl");

    public async Task<AppConfiguration> LoadConfigurationAsync(CancellationToken ct = default)
    {
        var config = await ReadAsync<AppConfiguration>(ConfigPath, ct) ?? AppConfiguration.Default;
        await SaveConfigurationAsync(config, ct);
        return config;
    }

    public Task SaveConfigurationAsync(AppConfiguration config, CancellationToken ct = default) =>
        WriteAsync(ConfigPath, config, ct);

    public async Task<SnapshotDocument> LoadSnapshotAsync(CancellationToken ct = default) =>
        await ReadAsync<SnapshotDocument>(SnapshotPath, ct) ?? new SnapshotDocument([], DateTimeOffset.MinValue);

    public Task SaveSnapshotAsync(SnapshotDocument snapshot, CancellationToken ct = default) =>
        WriteAsync(SnapshotPath, snapshot, ct);

    public async Task<Dictionary<string, Note>> LoadNotesAsync(CancellationToken ct = default) =>
        await ReadAsync<Dictionary<string, Note>>(NotesPath, ct) ?? [];

    public Task SaveNotesAsync(Dictionary<string, Note> notes, CancellationToken ct = default) =>
        WriteAsync(NotesPath, notes, ct);

    public async Task AppendActivityAsync(ActivityEntry entry, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        await using var stream = new FileStream(ActivityPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                         FileOptions.Asynchronous))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
        File.Move(temporaryPath, path, true);
    }
}
