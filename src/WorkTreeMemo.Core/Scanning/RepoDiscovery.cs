using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.Core.Scanning;

public sealed class RepoDiscovery
{
    public IReadOnlyList<RepoRef> Discover(IEnumerable<ScanRoot> roots, IEnumerable<string> excludedDirectoryNames,
        CancellationToken ct)
    {
        var results = new Dictionary<string, RepoRef>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(excludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => Directory.Exists(root.Path)))
            DiscoverRoot(root, excluded, results, ct);
        return results.Values.OrderBy(repo => repo.Name).ToList();
    }

    private static void DiscoverRoot(ScanRoot root, HashSet<string> excluded, Dictionary<string, RepoRef> results,
        CancellationToken ct)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((Path.GetFullPath(root.Path), 0));
        while (pending.TryDequeue(out var item))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(item.Path, ".git")) || Directory.Exists(Path.Combine(item.Path, ".git")))
                results.TryAdd(item.Path, new RepoRef(item.Path, Path.GetFileName(item.Path)));
            if (item.Depth >= root.MaxDepth) continue;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(item.Path))
                    if (!excluded.Contains(Path.GetFileName(child)) && !string.Equals(Path.GetFileName(child), ".git",
                            StringComparison.OrdinalIgnoreCase))
                        pending.Enqueue((child, item.Depth + 1));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
