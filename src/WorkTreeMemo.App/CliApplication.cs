using Spectre.Console;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Scanning;
using WorkTreeMemo.Core.Storage;
using WorkTreeMemo.App.Services;

namespace WorkTreeMemo.App;

internal static class CliApplication
{
    public static bool IsCliInvocation(string[] args) =>
        args.Length > 0 && !args[0].Equals("--gui", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        await using var services = Program.CreateServiceProvider();
        var store = services.GetRequiredService<AppDataStore>();
        var version = services.GetRequiredService<ApplicationVersionProvider>();
        var command = NormalizeCommand(args[0]);
        if (command is "status" or "scan" or "reindex" &&
            !await EnsureGitAvailableAsync(services.GetRequiredService<GitInstallationChecker>())) return 1;
        return command switch
        {
            "status" => await ShowStatusAsync(store, version),
            "scan" => await ScanAsync(store, services.GetRequiredService<ScanCoordinator>(), version, false),
            "reindex" => await ScanAsync(store, services.GetRequiredService<ScanCoordinator>(), version, true),
            "export-json" => await ExportAsync(store, args.Skip(1).FirstOrDefault(), true),
            "export-markdown" => await ExportAsync(store, args.Skip(1).FirstOrDefault(), false),
            "config" => await ConfigureAsync(store, args.Skip(1).ToArray()),
            "add-root" => await ConfigureAsync(store, ["add-root", .. args.Skip(1)]),
            "remove-root" => await ConfigureAsync(store, ["remove-root", .. args.Skip(1)]),
            "roots" => await ConfigureAsync(store, ["roots"]),
            "version" => ShowVersion(version),
            "help" => ShowHelp(),
            _ => ShowUnknownCommand(args[0])
        };
    }

    private static async Task<int> ScanAsync(AppDataStore store, ScanCoordinator coordinator, ApplicationVersionProvider version, bool reindex)
    {
        await AnsiConsole.Status()
            .StartAsync(reindex ? "[yellow]Reindexing repositories…[/]" : "[yellow]Scanning known repositories…[/]",
                async _ => await coordinator.ScanAsync(reindex));
        return await ShowStatusAsync(store, version);
    }

    private static async Task<int> ShowStatusAsync(AppDataStore store, ApplicationVersionProvider version)
    {
        var snapshot = await store.LoadSnapshotAsync();
        var notes = await store.LoadNotesAsync();
        var config = await store.LoadConfigurationAsync();
        var items = new WipClassifier().Classify(snapshot.Repositories, notes, config.StaleAfterDays,
            DateTimeOffset.UtcNow, config.IgnoreBranchesOlderThanDays);
        AnsiConsole.Write(new Rule($"[bold blue]WorkTreeMemo[/] [grey]v{Escape(version.Version)}[/]").LeftJustified());
        if (snapshot.UpdatedAt == DateTimeOffset.MinValue)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No snapshot yet.[/] Run [green]worktreememo reindex[/] after adding a root.");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded).AddColumn("[bold]Kind[/]").AddColumn("[bold]Repository[/]")
            .AddColumn("[bold]Branch / worktree[/]").AddColumn("[bold]Detail[/]");
        foreach (var item in items.OrderBy(item => item.Repository.Repo.Name).ThenBy(item => item.Kind))
            table.AddRow(KindMarkup(item.Kind), Escape(item.Repository.Repo.Name),
                Escape(item.Branch?.Name ?? item.Worktree?.Branch ??
                    Path.GetFileName(item.Worktree?.Path ?? item.Repository.Repo.Path)), Escape(item.Detail));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"[grey]Snapshot: {snapshot.UpdatedAt.LocalDateTime:g} · local Git data only · {snapshot.Repositories.Count} repositories[/]");
        return 0;
    }

    private static async Task<int> ExportAsync(AppDataStore store, string? path, bool asJson)
    {
        if (string.IsNullOrWhiteSpace(path)) return 2;
        var snapshot = await store.LoadSnapshotAsync();
        var notes = await store.LoadNotesAsync();
        var config = await store.LoadConfigurationAsync();
        var items = new WipClassifier().Classify(snapshot.Repositories, notes, config.StaleAfterDays,
            DateTimeOffset.UtcNow, config.IgnoreBranchesOlderThanDays);
        var output = Path.GetFullPath(path);
        if (asJson)
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        else
            await File.WriteAllLinesAsync(output, ["# WorkTreeMemo report", "", .. items.Select(item =>
                $"- **{item.Kind}** — {item.Repository.Repo.Name}: {item.Detail}")]);
        AnsiConsole.MarkupLine($"[green]Exported:[/] {Escape(output)}");
        return 0;
    }

    private static async Task<int> ConfigureAsync(AppDataStore store, string[] args)
    {
        if (args.Length > 0 && args[0].Equals("roots", StringComparison.OrdinalIgnoreCase))
        {
            var config = await store.LoadConfigurationAsync();
            var table = new Table().Border(TableBorder.Rounded).AddColumn("[bold]Path[/]").AddColumn("[bold]Depth[/]");
            foreach (var root in config.Roots.OrderBy(root => root.Path))
                table.AddRow(Escape(root.Path), root.MaxDepth.ToString());
            AnsiConsole.Write(table);
            return 0;
        }

        if (args.Length is >= 2 && args[0].Equals("add-root", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = NormalizePath(args[1]);
            if (!Directory.Exists(fullPath))
            {
                AnsiConsole.MarkupLine("[red]Root directory does not exist.[/]");
                return 2;
            }

            var config = await store.LoadConfigurationAsync();
            var depth = args.Length > 2 && int.TryParse(args[2], out var parsed) ? Math.Clamp(parsed, 0, 10) : 3;
            var roots = config.Roots
                .Where(root => !string.Equals(root.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                .Append(new ScanRoot(fullPath, depth)).ToList();
            await store.SaveConfigurationAsync(config with { Roots = roots });
            AnsiConsole.MarkupLine($"[green]Added root:[/] {Escape(fullPath)} (depth {depth})");
            return 0;
        }

        if (args.Length >= 2 && args[0].Equals("remove-root", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = NormalizePath(args[1]);
            var config = await store.LoadConfigurationAsync();
            var roots = config.Roots
                .Where(root => !string.Equals(root.Path, fullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (roots.Count == config.Roots.Count)
            {
                AnsiConsole.MarkupLine("[yellow]Root was not configured.[/]");
                return 2;
            }

            await store.SaveConfigurationAsync(config with { Roots = roots });
            AnsiConsole.MarkupLine($"[green]Removed root:[/] {Escape(fullPath)}");
            return 0;
        }

        AnsiConsole.MarkupLine(
            "[yellow]Usage:[/] WorkTreeMemo config roots | add-root <path> [depth] | remove-root <path>");
        return 2;
    }

    private static int ShowHelp()
    {
        AnsiConsole.Write(new Panel(
                "[green]WorkTreeMemo[/] [grey]status[/]  or  [green]WorkTreeMemo[/] [grey]--status[/]\n[green]WorkTreeMemo[/] [grey]scan[/]    or  [green]WorkTreeMemo[/] [grey]--scan[/]\n[green]WorkTreeMemo[/] [grey]reindex[/] or  [green]WorkTreeMemo[/] [grey]--reindex[/]\n[green]WorkTreeMemo[/] [grey]--version[/]\n[green]WorkTreeMemo[/] [grey]config roots[/]\n[green]WorkTreeMemo[/] [grey]config add-root <path> [[depth]][/]\n[green]WorkTreeMemo[/] [grey]config remove-root <path>[/]\n[green]WorkTreeMemo[/] [grey]--add-root <path> [[depth]][/]\n[green]WorkTreeMemo[/] [grey]--gui[/]")
            .Header("WorkTreeMemo CLI"));
        return 0;
    }

    private static string NormalizeCommand(string value) => value.ToLowerInvariant() switch
    {
        "--status" or "-s" => "status",
        "--scan" => "scan",
        "--reindex" or "-r" => "reindex",
        "--add-root" => "add-root",
        "--remove-root" => "remove-root",
        "--roots" => "roots",
        "--version" or "-v" or "version" => "version",
        "export-json" => "export-json",
        "export-markdown" or "export-md" => "export-markdown",
        "--help" or "-h" or "help" => "help",
        _ => value.ToLowerInvariant()
    };

    private static int ShowUnknownCommand(string value)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Escape(value)}");
        return ShowHelp() == 0 ? 2 : 2;
    }

    private static int ShowVersion(ApplicationVersionProvider version)
    {
        AnsiConsole.MarkupLine($"WorkTreeMemo [blue]v{Escape(version.Version)}[/]");
        return 0;
    }

    private static string KindMarkup(WipKind kind) => kind switch
    {
        WipKind.Dirty => "[yellow]Dirty[/]", WipKind.Unpushed => "[red]Unpushed[/]",
        WipKind.StaleUnmerged => "[orange1]Stale[/]", WipKind.Parked => "[blue]Parked[/]",
        _ => Escape(kind.ToString())
    };

    private static string Escape(string value) => Markup.Escape(value);

    private static async Task<bool> EnsureGitAvailableAsync(GitInstallationChecker gitInstallationChecker)
    {
        var status = await gitInstallationChecker.CheckAsync();
        if (status.IsAvailable) return true;
        AnsiConsole.Write(
            new Panel(
                    $"[red]Git CLI is required.[/]\n{Escape(status.Detail)}\n\nInstall Git and ensure [grey]git[/] is available on your PATH, then run the command again.")
                .Header("WorkTreeMemo cannot scan"));
        return false;
    }

    private static string NormalizePath(string value)
    {
        var path = value.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
            : value;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
