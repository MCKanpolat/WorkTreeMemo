using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.Core.Tests;

public sealed class DirectorySizeCalculatorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetSize_sums_files_in_nested_directories()
    {
        Directory.CreateDirectory(Path.Combine(_path, "nested"));
        File.WriteAllBytes(Path.Combine(_path, "first.bin"), new byte[123]);
        File.WriteAllBytes(Path.Combine(_path, "nested", "second.bin"), new byte[456]);

        var actual = DirectorySizeCalculator.GetSize(_path, CancellationToken.None);

        Assert.Equal(579, actual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
