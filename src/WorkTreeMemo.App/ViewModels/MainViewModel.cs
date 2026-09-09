using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Localization;
using WorkTreeMemo.Core.Scanning;
using WorkTreeMemo.Core.Storage;
using WorkTreeMemo.App.Services;

namespace WorkTreeMemo.App.ViewModels;

public sealed class MainViewModel(
    AppDataStore store,
    ScanCoordinator coordinator,
    GitInstallationChecker gitInstallationChecker,
    ApplicationVersionProvider applicationVersionProvider,
    GitHubReleaseUpdateChecker releaseUpdateChecker) : INotifyPropertyChanged
{
    private readonly WipClassifier _classifier = new();
    private readonly Localizer _localizer = new();
    private Dictionary<string, Note> _notes = [];
    private AppConfiguration _configuration = AppConfiguration.Default;
    private SnapshotDocument _snapshot = new([], DateTimeOffset.MinValue);

    private string _searchText = string.Empty;
    private string _noteText = string.Empty;
    private string _statusText = "Ready";
    private string _gitWarningText = string.Empty;
    private string _updateMessage = string.Empty;
    private string _newRootPath = string.Empty;
    private int _newRootDepth = 3;
    private bool _isParked;
    private bool _isBusy;
    private bool _isGitAvailable = true;
    private bool _isUpdateAvailable;
    private bool _isSettingsOpen;
    private FilterOption? _selectedFilter;
    private ScanRoot? _selectedRoot;
    private object? _selectedNode;
    private WipRow? _selectedItem;

    private ICommand? _reindexCommand;
    private ICommand? _saveNoteCommand;
    private ICommand? _addRootCommand;
    private ICommand? _removeRootCommand;
    private ICommand? _showWorkCommand;
    private ICommand? _showSettingsCommand;
    private ICommand? _clearSearchCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Localized strings, bound from XAML as <c>Text[Key]</c>.</summary>
    public Localizer Text => _localizer;

    public string Title => "WorkTreeMemo";
    public string VersionText => $"v{applicationVersionProvider.Version}";

    // ── Navigation ──────────────────────────────────────────────────────

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set
        {
            if (Set(ref _isSettingsOpen, value)) OnPropertyChanged(nameof(IsWorkOpen));
        }
    }

    public bool IsWorkOpen => !_isSettingsOpen;

    public ICommand ShowWorkCommand => _showWorkCommand ??= new RelayCommand(() => IsSettingsOpen = false);
    public ICommand ShowSettingsCommand => _showSettingsCommand ??= new RelayCommand(() => IsSettingsOpen = true);
    public ICommand ClearSearchCommand => _clearSearchCommand ??= new RelayCommand(() => SearchText = string.Empty);

    // ── Appearance ──────────────────────────────────────────────────────

    /// <summary>
    /// What the window is actually painted in. Until the stored preference has been
    /// read, that is whatever the operating system asked for at startup.
    /// </summary>
    private ThemePreference EffectiveTheme => _configuration.Theme ?? ThemeService.DetectOperatingSystemTheme();

    public bool IsLightTheme
    {
        get => EffectiveTheme == ThemePreference.Light;
        set
        {
            if (value) _ = UseThemeAsync(ThemePreference.Light);
        }
    }

    public bool IsDarkTheme
    {
        get => EffectiveTheme == ThemePreference.Dark;
        set
        {
            if (value) _ = UseThemeAsync(ThemePreference.Dark);
        }
    }

    private async Task UseThemeAsync(ThemePreference theme)
    {
        if (_configuration.Theme == theme) return;
        ThemeService.Apply(theme);
        _configuration = _configuration with { Theme = theme };
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        await store.SaveConfigurationAsync(_configuration);
    }

    // ── Header ──────────────────────────────────────────────────────────

    public string WorkspaceSummary => _snapshot.UpdatedAt == DateTimeOffset.MinValue
        ? _localizer["SummaryNotScanned"]
        : string.Format(_localizer["SummaryCounts"], VisibleCount, _snapshot.Repositories.Count);

    public string LastScanText => _snapshot.UpdatedAt == DateTimeOffset.MinValue
        ? _localizer["LastScanNever"]
        : $"{_localizer["LastScanPrefix"]} {_snapshot.UpdatedAt.LocalDateTime:g}";

    public string RootCountText => string.Format(_localizer["RootCount"], Roots.Count);

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public bool IsGitAvailable
    {
        get => _isGitAvailable;
        private set
        {
            if (Set(ref _isGitAvailable, value)) OnPropertyChanged(nameof(IsGitUnavailable));
        }
    }

    public bool IsGitUnavailable => !IsGitAvailable;

    public string GitWarningText
    {
        get => _gitWarningText;
        private set => Set(ref _gitWarningText, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => Set(ref _isUpdateAvailable, value);
    }

    public string UpdateMessage
    {
        get => _updateMessage;
        private set => Set(ref _updateMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    // ── Work queue ──────────────────────────────────────────────────────

    public ObservableCollection<FilterOption> Filters { get; } = CreateFilters();
    public ObservableCollection<RepoGroup> Groups { get; } = [];
    public ObservableCollection<ScanRoot> Roots { get; } = [];

    public int VisibleCount { get; private set; }

    /// <summary>No scan roots yet: the user has nothing to look at until they add one.</summary>
    public bool ShowRootsInvitation => Roots.Count == 0 && Groups.Count == 0;

    /// <summary>Roots exist but the current filter and search matched nothing.</summary>
    public bool ShowNoMatches => Roots.Count > 0 && Groups.Count == 0;

    public bool ShowResults => Groups.Count > 0;

    public FilterOption? SelectedFilter
    {
        get => _selectedFilter ??= Filters[0];
        set
        {
            if (value is null || !Set(ref _selectedFilter, value)) return;
            IsSettingsOpen = false;
            RefreshItems();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearchText));
                RefreshItems();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrEmpty(_searchText);

    /// <summary>The tree holds both groups and rows, so the bound selection is untyped.</summary>
    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (Set(ref _selectedNode, value)) SelectedItem = value as WipRow;
        }
    }

    public WipRow? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (!Set(ref _selectedItem, value)) return;
            NoteText = value?.Note?.Text ?? string.Empty;
            IsParked = value?.Note?.IsParked ?? false;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanTakeNote));
            OnPropertyChanged(nameof(SelectedDetail));
        }
    }

    public bool HasSelection => _selectedItem is not null;

    /// <summary>
    /// Notes are keyed by branch, so items that come from a worktree rather than a
    /// branch cannot carry one. Offering the editor for those would save nothing.
    /// </summary>
    public bool CanTakeNote => _selectedItem?.Branch is not null;

    public string SelectedDetail => _selectedItem is null
        ? string.Empty
        : $"{_selectedItem.Detail}\n\n{_localizer["LocalGitOnly"]}";

    public string NoteText
    {
        get => _noteText;
        set => Set(ref _noteText, value);
    }

    public bool IsParked
    {
        get => _isParked;
        set => Set(ref _isParked, value);
    }

    // ── Settings ────────────────────────────────────────────────────────

    public string NewRootPath
    {
        get => _newRootPath;
        set => Set(ref _newRootPath, value);
    }

    public int NewRootDepth
    {
        get => _newRootDepth;
        set => Set(ref _newRootDepth, Math.Clamp(value, 0, 10));
    }

    public ScanRoot? SelectedRoot
    {
        get => _selectedRoot;
        set => Set(ref _selectedRoot, value);
    }

    /// <summary>Branches untouched for longer than this are hidden. Zero turns the cutoff off.</summary>
    public int IgnoreBranchesOlderThanDays
    {
        get => _configuration.IgnoreBranchesOlderThanDays ?? 0;
        set
        {
            var days = Math.Clamp(value, 0, 3650);
            if (days == IgnoreBranchesOlderThanDays) return;
            _configuration = _configuration with { IgnoreBranchesOlderThanDays = days == 0 ? null : days };
            OnPropertyChanged();
            OnPropertyChanged(nameof(BranchCutoffSummary));
            RefreshItems();
            _ = store.SaveConfigurationAsync(_configuration);
        }
    }

    public string BranchCutoffSummary => IgnoreBranchesOlderThanDays == 0
        ? _localizer["CutoffOff"]
        : string.Format(_localizer["CutoffOn"], IgnoreBranchesOlderThanDays);

    // ── Commands ────────────────────────────────────────────────────────

    public ICommand ReindexCommand => _reindexCommand ??= new AsyncCommand(() => ScanAsync(true));
    public ICommand SaveNoteCommand => _saveNoteCommand ??= new AsyncCommand(SaveNoteAsync);
    public ICommand AddRootCommand => _addRootCommand ??= new AsyncCommand(AddRootAsync);
    public ICommand RemoveRootCommand => _removeRootCommand ??= new AsyncCommand(RemoveRootAsync);

    // ── Lifecycle ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _notes = await store.LoadNotesAsync();
        _configuration = await store.LoadConfigurationAsync();
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IgnoreBranchesOlderThanDays));
        OnPropertyChanged(nameof(BranchCutoffSummary));
        RefreshRoots();
        _snapshot = await store.LoadSnapshotAsync();
        RefreshItems();
        _ = CheckForUpdatesAsync();
        var gitStatus = await gitInstallationChecker.CheckAsync();
        IsGitAvailable = gitStatus.IsAvailable;
        if (!gitStatus.IsAvailable)
        {
            GitWarningText = $"Git CLI is required to scan repositories. {gitStatus.Detail}";
            StatusText = "Git is unavailable.";
            return;
        }

        await ScanAsync(false);
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await releaseUpdateChecker.CheckAsync();
        if (result is not { IsUpdateAvailable: true }) return;
        IsUpdateAvailable = true;
        UpdateMessage = $"Version {result.LatestVersion} is available on GitHub.";
    }

    public async Task ScanAsync(bool reindex)
    {
        if (!IsGitAvailable)
        {
            StatusText = "Git is unavailable. Install Git and restart WorkTreeMemo.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = reindex ? "Reindexing…" : "Scanning…";
            _snapshot = await coordinator.ScanAsync(reindex);
            RefreshItems();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            if (!StatusText.StartsWith("Scan failed", StringComparison.Ordinal))
                StatusText = $"Ready — {_snapshot.Repositories.Count} repositories";
            OnPropertyChanged(nameof(LastScanText));
        }
    }

    public void ApplySnapshot(SnapshotDocument snapshot)
    {
        _snapshot = snapshot;
        RefreshItems();
        StatusText = $"Ready — {_snapshot.Repositories.Count} repositories";
    }

    private async Task SaveNoteAsync()
    {
        if (SelectedItem?.Branch is null) return;
        _notes[WipClassifier.NoteKey(SelectedItem.Repository.Repo.Path, SelectedItem.Branch.Name)] =
            new(NoteText, IsParked, DateTimeOffset.UtcNow);
        await store.SaveNotesAsync(_notes);
        RefreshItems();
        StatusText = "Note saved.";
    }

    private async Task AddRootAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRootPath))
        {
            StatusText = "Enter a folder path.";
            return;
        }

        var path = NormalizePath(NewRootPath);
        if (!Directory.Exists(path))
        {
            StatusText = "Folder does not exist.";
            return;
        }

        _configuration = _configuration with
        {
            Roots = _configuration.Roots
                .Where(root => !string.Equals(root.Path, path, StringComparison.OrdinalIgnoreCase))
                .Append(new ScanRoot(path, NewRootDepth)).ToList()
        };
        await store.SaveConfigurationAsync(_configuration);
        NewRootPath = string.Empty;
        RefreshRoots();
        StatusText = "Root added. Run Reindex to discover repositories.";
    }

    private async Task RemoveRootAsync()
    {
        if (SelectedRoot is null) return;
        _configuration = _configuration with
        {
            Roots = _configuration.Roots.Where(root =>
                !string.Equals(root.Path, SelectedRoot.Path, StringComparison.OrdinalIgnoreCase)).ToList()
        };
        await store.SaveConfigurationAsync(_configuration);
        SelectedRoot = null;
        RefreshRoots();
        StatusText = "Root removed.";
    }

    private void RefreshRoots()
    {
        Roots.Clear();
        foreach (var root in _configuration.Roots.OrderBy(root => root.Path)) Roots.Add(root);
        OnPropertyChanged(nameof(RootCountText));
        OnPropertyChanged(nameof(HasRoots));
        OnPropertyChanged(nameof(ShowRootsInvitation));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    public bool HasRoots => Roots.Count > 0;

    private void RefreshItems()
    {
        var matching = _classifier.Classify(_snapshot.Repositories, _notes, _configuration.StaleAfterDays,
                DateTimeOffset.UtcNow, _configuration.IgnoreBranchesOlderThanDays)
            .Select(item => new WipRow(item, _localizer,
                item.Branch is null
                    ? null
                    : _notes.GetValueOrDefault(WipClassifier.NoteKey(item.Repository.Repo.Path, item.Branch.Name))))
            .Where(row => string.IsNullOrWhiteSpace(SearchText) ||
                          row.Summary.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Counts describe the search results, so they stay meaningful while a filter is active.
        foreach (var filter in Filters)
            filter.Count = filter.Kind is null ? matching.Count : matching.Count(row => row.Kind == filter.Kind);

        var kind = SelectedFilter?.Kind;
        var visible = matching.Where(row => kind is null || row.Kind == kind).ToList();
        VisibleCount = visible.Count;

        Groups.Clear();
        foreach (var group in visible
                     .GroupBy(row => row.Repository.Repo)
                     .OrderBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Select(group => new RepoGroup(group.Key.Name, group.Key.Path, _localizer,
                         group.OrderBy(row => row.Kind).ThenBy(row => row.ScopeName,
                             StringComparer.CurrentCultureIgnoreCase).ToList())))
            Groups.Add(group);

        if (_selectedItem is not null && !visible.Contains(_selectedItem)) SelectedNode = null;

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(WorkspaceSummary));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(ShowRootsInvitation));
    }

    private static ObservableCollection<FilterOption> CreateFilters()
    {
        var text = new Localizer();
        return
        [
            new(text["FilterAll"], null),
            new(text["KindDirty"], WipKind.Dirty),
            new(text["KindUnpushed"], WipKind.Unpushed),
            new(text["KindStashed"], WipKind.Stashed),
            new(text["KindStale"], WipKind.StaleUnmerged),
            new(text["KindParked"], WipKind.Parked),
            new(text["KindClean"], WipKind.CleanCandidate)
        ];
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string NormalizePath(string value)
    {
        var path = value.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
            : value;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}

/// <summary>One entry in the sidebar work queue, carrying a live count.</summary>
public sealed class FilterOption(string label, WipKind? kind) : INotifyPropertyChanged
{
    private int _count;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; } = label;
    public WipKind? Kind { get; } = kind;

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }
}

/// <summary>The work items of a single repository, shown as one collapsible section.</summary>
public sealed class RepoGroup(string name, string path, Localizer text, IReadOnlyList<WipRow> items)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string OpenFolderText { get; } = text["OpenFolder"];
    public IReadOnlyList<WipRow> Items { get; } = items;
    public string CountText { get; } = items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

public sealed record WipRow(WipItem Item, Localizer Text, Note? Note)
{
    public RepoSnapshot Repository => Item.Repository;
    public BranchState? Branch => Item.Branch;
    public WipKind Kind => Item.Kind;
    public string Detail => Item.Detail;
    public string RepositoryName => Repository.Repo.Name;

    public string ScopeName => Branch?.Name ??
                               Item.Worktree?.Branch ?? Path.GetFileName(Item.Worktree?.Path ?? Repository.Repo.Path);

    public string KindLabel => Kind switch
    {
        WipKind.Dirty => Text["BadgeDirty"],
        WipKind.Unpushed => Text["BadgeUnpushed"],
        WipKind.Stashed => Text["BadgeStashed"],
        WipKind.StaleUnmerged => Text["BadgeStale"],
        WipKind.Parked => Text["BadgeParked"],
        WipKind.CleanCandidate => Text["BadgeClean"],
        _ => Kind.ToString().ToUpperInvariant()
    };

    // Drive the badge colour through style classes so it follows the theme.
    public bool IsDirty => Kind == WipKind.Dirty;
    public bool IsUnpushed => Kind == WipKind.Unpushed;
    public bool IsStashed => Kind == WipKind.Stashed;
    public bool IsStale => Kind == WipKind.StaleUnmerged;
    public bool IsClean => Kind == WipKind.CleanCandidate;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note?.Text);
    public string NoteText => Note?.Text ?? string.Empty;
    public string OpenFolderText => Text["OpenFolder"];

    public string Summary => $"{Repository.Repo.Name} — {ScopeName}: {Detail}";
}

internal sealed class AsyncCommand(Func<Task> action) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await action();
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal sealed class RelayCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
