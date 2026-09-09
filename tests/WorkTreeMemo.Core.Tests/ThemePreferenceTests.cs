using System.Text.Json;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.Core.Tests;

public sealed class ThemePreferenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Default_configuration_leaves_the_theme_undecided_so_the_first_launch_reads_the_operating_system()
        => Assert.Null(AppConfiguration.Default.Theme);

    [Theory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public async Task Theme_round_trips_through_the_configuration_file(ThemePreference theme)
    {
        var store = new AppDataStore(_path);
        await store.SaveConfigurationAsync(AppConfiguration.Default with { Theme = theme });
        Assert.Equal(theme, (await store.LoadConfigurationAsync()).Theme);
    }

    [Fact]
    public async Task Theme_is_written_as_a_readable_name_rather_than_a_number()
    {
        var store = new AppDataStore(_path);
        await store.SaveConfigurationAsync(AppConfiguration.Default with { Theme = ThemePreference.Dark });
        var json = await File.ReadAllTextAsync(Path.Combine(_path, "config.json"));
        Assert.Contains("\"Dark\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_written_before_themes_existed_still_loads_with_an_undecided_theme()
    {
        Directory.CreateDirectory(_path);
        var legacy = JsonSerializer.Serialize(new
        {
            roots = Array.Empty<object>(),
            excludedDirectoryNames = new[] { "node_modules" },
            baseBranches = new[] { "main" },
            scanInterval = "00:10:00",
            maxParallelism = 4,
            staleAfterDays = 7,
            culture = "en-US"
        });
        await File.WriteAllTextAsync(Path.Combine(_path, "config.json"), legacy);

        Assert.Null((await new AppDataStore(_path).LoadConfigurationAsync()).Theme);
    }

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
