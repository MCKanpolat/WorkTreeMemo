namespace WorkTreeMemo.Core.Scanning;

/// <summary>Reads the size of a repository without following symbolic links or junctions.</summary>
public static class DirectorySizeCalculator
{
    public static long GetSize(string rootPath, CancellationToken ct)
    {
        long total = 0;
        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    try { total = checked(total + new FileInfo(file).Length); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            directories.Push(child);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return total;
    }
}
