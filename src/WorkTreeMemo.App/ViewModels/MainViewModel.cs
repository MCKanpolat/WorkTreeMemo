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
    private IReadOnlyList<ActivityEntry> _activities = [];

    private string _searchText = string.Empty;
    private string _noteText = string.Empty;
    private string _nextStepText = string.Empty;
    private string _localPathText = string.Empty;
    private string _snoozeUntilText = string.Empty;
    private string _statusText = string.Empty;
    private string _gitWarningText = string.Empty;
    private string _updateMessage = string.Empty;
    private string _newRootPath = string.Empty;
    private string _newSavedViewName = string.Empty;
    private int _newRootDepth = 3;
    private bool _isParked;
    private bool _isBusy;
    private bool _isGitAvailable = true;
    private bool _isUpdateAvailable;
    private AppPage _page;
    private bool _isSortAscending = true;
    private FilterOption? _selectedFilter;
    private SortOption? _selectedSortOption;
    private ScanRoot? _selectedRoot;
    private SavedView? _selectedSavedView;
    private object? _selectedNode;
    private WipRow? _selectedItem;

    private ICommand? _reindexCommand;
    private ICommand? _saveNoteCommand;
    private ICommand? _addRootCommand;
    private ICommand? _removeRootCommand;
    private ICommand? _showWorkCommand;
    private ICommand? _showSettingsCommand;
    private ICommand? _showAboutCommand;
    private ICommand? _checkForUpdatesCommand;
    private ICommand? _clearSearchCommand;
    private ICommand? _toggleSortDirectionCommand;
    private ICommand? _saveViewCommand;
    private ICommand? _applyViewCommand;
    private ICommand? _snoozeTomorrowCommand;
    private ICommand? _clearSnoozeCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Localized strings, bound from XAML as <c>Text[Key]</c>.</summary>
    public Localizer Text => _localizer;

    public string Title => "WorkTreeMemo";
    public string VersionText => $"v{applicationVersionProvider.Version}";
    public bool HasRecentChanges => _activities.Count > 0;
    public string RecentChangesText => string.Join(Environment.NewLine, _activities.OrderByDescending(activity => activity.DetectedAt)
        .Take(5).Select(activity => $"{activity.DetectedAt.LocalDateTime:g} · {Path.GetFileName(activity.RepoPath)} · {activity.Description}"));
    public string ScanHealthText
    {
        get
        {
            var failures = _snapshot.Repositories.Where(repository => repository.Error is not null).ToList();
            return failures.Count == 0
                ? _localizer["ScanHealthGood"]
                : string.Format(_localizer["ScanHealthErrors"], failures.Count, string.Join(", ", failures.Take(3).Select(repository => repository.Repo.Name)));
        }
    }

    // ── Navigation ──────────────────────────────────────────────────────

    public bool IsSettingsOpen
    {
        get => _page == AppPage.Settings;
        private set => NavigateTo(value ? AppPage.Settings : AppPage.Work);
    }

    public bool IsAboutOpen
    {
        get => _page == AppPage.About;
        private set => NavigateTo(value ? AppPage.About : AppPage.Work);
    }

    public bool IsWorkOpen => _page == AppPage.Work;

    public ICommand ShowWorkCommand => _showWorkCommand ??= new RelayCommand(() => NavigateTo(AppPage.Work));
    public ICommand ShowSettingsCommand => _showSettingsCommand ??= new RelayCommand(() => NavigateTo(AppPage.Settings));
    public ICommand ShowAboutCommand => _showAboutCommand ??= new RelayCommand(() => NavigateTo(AppPage.About));
    public ICommand ClearSearchCommand => _clearSearchCommand ??= new RelayCommand(() => SearchText = string.Empty);
    public ICommand ToggleSortDirectionCommand => _toggleSortDirectionCommand ??= new RelayCommand(() =>
        IsSortAscending = !IsSortAscending);
    public ICommand SnoozeTomorrowCommand => _snoozeTomorrowCommand ??= new AsyncCommand(() => SetSnoozeAsync(DateTimeOffset.UtcNow.AddDays(1)));
    public ICommand ClearSnoozeCommand => _clearSnoozeCommand ??= new AsyncCommand(() => SetSnoozeAsync(null));

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

    public string UpdateCheckStatus { get; private set; } = string.Empty;

    public bool IsCheckingForUpdates { get; private set; }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    // ── Work queue ──────────────────────────────────────────────────────

    public ObservableCollection<FilterOption> Filters { get; } = CreateFilters();
    public ObservableCollection<SortOption> SortOptions { get; } = CreateSortOptions();
    public ObservableCollection<RepoGroup> Groups { get; } = [];
    public ObservableCollection<ScanRoot> Roots { get; } = [];
    public ObservableCollection<SavedView> SavedViews { get; } = [];

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

    public SortOption? SelectedSortOption
    {
        get => _selectedSortOption ??= SortOptions[0];
        set
        {
            if (value is not null && Set(ref _selectedSortOption, value)) RefreshItems();
        }
    }

    public bool IsSortAscending
    {
        get => _isSortAscending;
        set
        {
            if (Set(ref _isSortAscending, value))
            {
                OnPropertyChanged(nameof(SortDirectionGlyph));
                OnPropertyChanged(nameof(SortDirectionToolTip));
                RefreshItems();
            }
        }
    }

    public string SortDirectionGlyph => IsSortAscending ? "↑" : "↓";
    public string SortDirectionToolTip => IsSortAscending ? _localizer["SortAscending"] : _localizer["SortDescending"];

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
            NextStepText = value?.Note?.NextStep ?? string.Empty;
            LocalPathText = value?.Note?.LocalPath ?? string.Empty;
            SnoozeUntilText = value?.Note?.SnoozedUntil?.LocalDateTime.ToString("yyyy-MM-dd") ?? string.Empty;
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
        : $"{_selectedItem.Detail}\n{_selectedItem.EvidenceText}\n\n{_localizer["Repository"]}: {_selectedItem.Repository.Repo.Path}\n{_localizer["Worktree"]}: {_selectedItem.WorktreePath}\n{_localizer["DiskSize"]}: {_selectedItem.DiskSizeText}\n{_localizer["LocalGitOnly"]}";

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

    public string NextStepText { get => _nextStepText; set => Set(ref _nextStepText, value); }
    public string LocalPathText { get => _localPathText; set => Set(ref _localPathText, value); }
    public string SnoozeUntilText { get => _snoozeUntilText; set => Set(ref _snoozeUntilText, value); }

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

    public string NewSavedViewName { get => _newSavedViewName; set => Set(ref _newSavedViewName, value); }
    public SavedView? SelectedSavedView { get => _selectedSavedView; set => Set(ref _selectedSavedView, value); }

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
    public ICommand SaveViewCommand => _saveViewCommand ??= new AsyncCommand(SaveViewAsync);
    public ICommand ApplyViewCommand => _applyViewCommand ??= new RelayCommand(ApplySelectedView);
    public ICommand CheckForUpdatesCommand => _checkForUpdatesCommand ??= new AsyncCommand(CheckForUpdatesAsync);

    // ── Lifecycle ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _notes = await store.LoadNotesAsync();
        _activities = await store.LoadActivityAsync();
        OnPropertyChanged(nameof(HasRecentChanges));
        OnPropertyChanged(nameof(RecentChangesText));
        OnPropertyChanged(nameof(ScanHealthText));
        _configuration = await store.LoadConfigurationAsync();
        RefreshSavedViews();
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
            GitWarningText = string.Format(_localizer["GitRequired"], gitStatus.Detail);
            StatusText = _localizer["GitUnavailable"];
            return;
        }

        if (NeedsStartupScan())
            await ScanAsync(false);
        else
            StatusText = ReadyStatus;
    }

    private bool NeedsStartupScan() =>
        _snapshot.UpdatedAt == DateTimeOffset.MinValue ||
        DateTimeOffset.UtcNow >= _snapshot.UpdatedAt + _configuration.ScanInterval;

    private string ReadyStatus => string.Format(_localizer["ReadyStatus"], _snapshot.Repositories.Count);

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        OnPropertyChanged(nameof(IsCheckingForUpdates));
        UpdateCheckStatus = _localizer["CheckingForUpdates"];
        OnPropertyChanged(nameof(UpdateCheckStatus));
        try
        {
            var result = await releaseUpdateChecker.CheckAsync();
            if (result is null)
            {
                UpdateCheckStatus = _localizer["UpdateCheckFailed"];
                return;
            }

            if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                UpdateMessage = string.Format(_localizer["UpdateAvailable"], result.LatestVersion);
                UpdateCheckStatus = string.Format(_localizer["UpdateAvailable"], result.LatestVersion);
                return;
            }

            IsUpdateAvailable = false;
            UpdateCheckStatus = _localizer["UpdateIsCurrent"];
        }
        finally
        {
            IsCheckingForUpdates = false;
            OnPropertyChanged(nameof(IsCheckingForUpdates));
            OnPropertyChanged(nameof(UpdateCheckStatus));
        }
    }

    public async Task ScanAsync(bool reindex)
    {
        if (!IsGitAvailable)
        {
            StatusText = _localizer["GitUnavailableRestart"];
            return;
        }

        var scanFailed = false;
        try
        {
            IsBusy = true;
            StatusText = reindex ? _localizer["Reindexing"] : _localizer["Scanning"];
            var snapshot = await coordinator.ScanAsync(reindex);
            if (!ReferenceEquals(_snapshot, snapshot)) ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            StatusText = _localizer["ScanCancelled"];
        }
        catch (Exception ex)
        {
            scanFailed = true;
            StatusText = string.Format(_localizer["ScanFailed"], ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (!scanFailed) StatusText = ReadyStatus;
            OnPropertyChanged(nameof(LastScanText));
        }
    }

    public async Task ExcludeRepositoryAsync(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var excludedPaths = (_configuration.ExcludedRepositoryPaths ?? [])
            .Append(normalizedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _configuration = _configuration with { ExcludedRepositoryPaths = excludedPaths };
        await store.SaveConfigurationAsync(_configuration);
        RefreshItems();
        StatusText = _localizer["RepositoryExcluded"];
    }

    public async Task ToggleFavoriteAsync(RepoGroup group)
    {
        var favorites = (_configuration.FavoriteRepositoryPaths ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!favorites.Add(group.Path)) favorites.Remove(group.Path);
        _configuration = _configuration with { FavoriteRepositoryPaths = favorites.ToList() };
        await store.SaveConfigurationAsync(_configuration);
        RefreshItems();
        StatusText = group.IsFavorite ? _localizer["FavoriteRemoved"] : _localizer["FavoriteAdded"];
    }

    public async Task ToggleFlagAsync(WipRow row)
    {
        if (row.Branch is null) return;
        var key = WipClassifier.NoteKey(row.Repository.Repo.Path, row.Branch.Name);
        var current = _notes.GetValueOrDefault(key) ?? new Note(string.Empty, false, DateTimeOffset.UtcNow);
        _notes[key] = current with { IsFlagged = !current.IsFlagged, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveNotesAsync(_notes);
        RefreshItems();
        StatusText = row.IsFlagged ? _localizer["FlagRemoved"] : _localizer["FlagAdded"];
    }

    public void ApplySnapshot(SnapshotDocument snapshot)
    {
        if (ReferenceEquals(_snapshot, snapshot)) return;
        _snapshot = snapshot;
        RefreshItems();
        _ = RefreshActivityAsync();
        StatusText = ReadyStatus;
    }

    private async Task RefreshActivityAsync()
    {
        _activities = await store.LoadActivityAsync();
        OnPropertyChanged(nameof(HasRecentChanges));
        OnPropertyChanged(nameof(RecentChangesText));
        OnPropertyChanged(nameof(ScanHealthText));
    }

    private async Task SaveNoteAsync()
    {
        if (SelectedItem?.Branch is null) return;
        var key = WipClassifier.NoteKey(SelectedItem.Repository.Repo.Path, SelectedItem.Branch.Name);
        var current = _notes.GetValueOrDefault(key);
        var snoozedUntil = DateTimeOffset.TryParse(SnoozeUntilText, out var parsedSnooze) ? parsedSnooze : current?.SnoozedUntil;
        _notes[key] = new(NoteText, IsParked, DateTimeOffset.UtcNow, current?.IsFlagged ?? false, snoozedUntil,
            NullIfEmpty(NextStepText), NullIfEmpty(LocalPathText));
        await store.SaveNotesAsync(_notes);
        RefreshItems();
        StatusText = _localizer["NoteSaved"];
    }

    private async Task SetSnoozeAsync(DateTimeOffset? until)
    {
        if (SelectedItem?.Branch is null) return;
        var key = WipClassifier.NoteKey(SelectedItem.Repository.Repo.Path, SelectedItem.Branch.Name);
        var current = _notes.GetValueOrDefault(key) ?? new Note(string.Empty, false, DateTimeOffset.UtcNow);
        _notes[key] = current with { SnoozedUntil = until, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveNotesAsync(_notes);
        SnoozeUntilText = until?.LocalDateTime.ToString("yyyy-MM-dd") ?? string.Empty;
        RefreshItems();
    }

    private async Task AddRootAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRootPath))
        {
            StatusText = _localizer["EnterFolderPath"];
            return;
        }

        var path = NormalizePath(NewRootPath);
        if (!Directory.Exists(path))
        {
            StatusText = _localizer["FolderDoesNotExist"];
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
        StatusText = _localizer["RootAdded"];
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
        StatusText = _localizer["RootRemoved"];
    }

    private async Task SaveViewAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSavedViewName)) return;
        var filter = SelectedFilter ?? Filters[0];
        var view = new SavedView(NewSavedViewName.Trim(), SearchText, filter.Kind, filter.FavoritesOnly, filter.FlaggedOnly);
        var views = (_configuration.SavedViews ?? []).Where(saved => !string.Equals(saved.Name, view.Name, StringComparison.OrdinalIgnoreCase))
            .Append(view).ToList();
        _configuration = _configuration with { SavedViews = views };
        await store.SaveConfigurationAsync(_configuration);
        NewSavedViewName = string.Empty;
        RefreshSavedViews();
    }

    private void ApplySelectedView()
    {
        if (SelectedSavedView is null) return;
        SearchText = SelectedSavedView.SearchText;
        SelectedFilter = Filters.FirstOrDefault(filter => filter.Kind == SelectedSavedView.Kind &&
            filter.FavoritesOnly == SelectedSavedView.FavoritesOnly && filter.FlaggedOnly == SelectedSavedView.FlaggedOnly) ?? Filters[0];
        IsSettingsOpen = false;
    }

    private void RefreshSavedViews()
    {
        SavedViews.Clear();
        foreach (var view in (_configuration.SavedViews ?? []).OrderBy(view => view.Name)) SavedViews.Add(view);
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
            .Where(row => !(_configuration.ExcludedRepositoryPaths ?? []).Contains(row.Repository.Repo.Path,
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Counts describe the search results, so they stay meaningful while a filter is active.
        foreach (var filter in Filters)
            filter.Count = filter.FavoritesOnly
                ? matching.Where(row => IsFavoriteRepository(row.Repository.Repo.Path))
                    .Select(row => row.Repository.Repo.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                : matching.Count(row => MatchesFilter(row, filter));

        var visible = matching.Where(row => SelectedFilter is null || MatchesFilter(row, SelectedFilter)).ToList();
        VisibleCount = visible.Count;

        Groups.Clear();
        var groups = visible.GroupBy(row => row.Repository.Repo);
        var orderedGroups = SortGroups(groups);
        foreach (var group in orderedGroups.Select(group => new RepoGroup(group.Key.Name, group.Key.Path, _localizer,
                     IsFavoriteRepository(group.Key.Path), SortRows(group).ToList())))
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
            new(text["FilterFavorites"], null, false, true),
            new(text["FilterFlagged"], null, true),
            new(text["KindDirty"], WipKind.Dirty),
            new(text["KindUnpushed"], WipKind.Unpushed),
            new(text["KindStashed"], WipKind.Stashed),
            new(text["KindStale"], WipKind.StaleUnmerged),
            new(text["KindParked"], WipKind.Parked),
            new(text["KindClean"], WipKind.CleanCandidate)
        ];
    }

    private bool MatchesFilter(WipRow row, FilterOption filter) =>
        (filter.Kind is null || row.Kind == filter.Kind) &&
        (!filter.FlaggedOnly || row.IsFlagged) &&
        (!filter.FavoritesOnly || IsFavoriteRepository(row.Repository.Repo.Path));

    private static ObservableCollection<SortOption> CreateSortOptions()
    {
        var text = new Localizer();
        return
        [
            new(text["SortRepository"], WorkQueueSort.Repository),
            new(text["SortBranch"], WorkQueueSort.Branch),
            new(text["SortStatus"], WorkQueueSort.Status),
            new(text["SortLastCommit"], WorkQueueSort.LastCommit),
            new(text["SortDirectoryModified"], WorkQueueSort.DirectoryModified)
        ];
    }

    private IOrderedEnumerable<IGrouping<RepoRef, WipRow>> SortGroups(IEnumerable<IGrouping<RepoRef, WipRow>> groups)
    {
        var favoritesFirst = groups.OrderByDescending(group => IsFavoriteRepository(group.Key.Path));
        return SelectedSortOption?.Value switch
        {
            WorkQueueSort.LastCommit => IsSortAscending
                ? favoritesFirst.ThenBy(LatestCommitAt)
                : favoritesFirst.ThenByDescending(LatestCommitAt),
            WorkQueueSort.DirectoryModified => IsSortAscending
                ? favoritesFirst.ThenBy(group => group.First().Repository.DirectoryModifiedAt)
                : favoritesFirst.ThenByDescending(group => group.First().Repository.DirectoryModifiedAt),
            _ => IsSortAscending
                ? favoritesFirst.ThenBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
                : favoritesFirst.ThenByDescending(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private bool IsFavoriteRepository(string path) =>
        (_configuration.FavoriteRepositoryPaths ?? []).Contains(path, StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset? LatestCommitAt(IEnumerable<WipRow> rows) => rows
        .Select(row => row.Repository.Branches.Where(branch => branch.LastCommitAt is not null)
            .Select(branch => branch.LastCommitAt).DefaultIfEmpty().Max())
        .DefaultIfEmpty().Max();

    private IEnumerable<WipRow> SortRows(IEnumerable<WipRow> rows) => SelectedSortOption?.Value switch
    {
        WorkQueueSort.Branch => IsSortAscending
            ? rows.OrderBy(row => row.ScopeName, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderByDescending(row => row.ScopeName, StringComparer.CurrentCultureIgnoreCase),
        WorkQueueSort.Status => IsSortAscending
            ? rows.OrderBy(row => row.Kind).ThenBy(row => row.ScopeName, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderByDescending(row => row.Kind).ThenByDescending(row => row.ScopeName,
                StringComparer.CurrentCultureIgnoreCase),
        WorkQueueSort.LastCommit => IsSortAscending
            ? rows.OrderBy(row => row.Branch?.LastCommitAt)
            : rows.OrderByDescending(row => row.Branch?.LastCommitAt),
        WorkQueueSort.DirectoryModified => IsSortAscending
            ? rows.OrderBy(row => row.Repository.DirectoryModifiedAt)
            : rows.OrderByDescending(row => row.Repository.DirectoryModifiedAt),
        _ => rows.OrderBy(row => row.Kind).ThenBy(row => row.ScopeName, StringComparer.CurrentCultureIgnoreCase)
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void NavigateTo(AppPage page)
    {
        if (_page == page) return;
        _page = page;
        OnPropertyChanged(nameof(IsWorkOpen));
        OnPropertyChanged(nameof(IsSettingsOpen));
        OnPropertyChanged(nameof(IsAboutOpen));
    }

    private static string NormalizePath(string value)
    {
        var path = value.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
            : value;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private enum AppPage
    {
        Work,
        Settings,
        About
    }
}

/// <summary>One entry in the sidebar work queue, carrying a live count.</summary>
public sealed class FilterOption(string label, WipKind? kind, bool flaggedOnly = false, bool favoritesOnly = false)
    : INotifyPropertyChanged
{
    private int _count;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; } = label;
    public WipKind? Kind { get; } = kind;
    public bool FlaggedOnly { get; } = flaggedOnly;
    public bool FavoritesOnly { get; } = favoritesOnly;

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

public enum WorkQueueSort
{
    Repository,
    Branch,
    Status,
    LastCommit,
    DirectoryModified
}

public sealed class SortOption(string label, WorkQueueSort value)
{
    public string Label { get; } = label;
    public WorkQueueSort Value { get; } = value;
}

/// <summary>The work items of a single repository, shown as one collapsible section.</summary>
public sealed class RepoGroup(string name, string path, Localizer text, bool isFavorite, IReadOnlyList<WipRow> items)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string OpenFolderText { get; } = text["OpenFolder"];
    public string ExcludeText { get; } = text["ExcludeRepository"];
    public bool IsFavorite { get; } = isFavorite;
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteToolTip => text[IsFavorite ? "RemoveFavorite" : "AddFavorite"];
    public IReadOnlyList<WipRow> Items { get; } = items;
    public string CountText { get; } = items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

public sealed record WipRow(WipItem Item, Localizer Text, Note? Note)
{
    public RepoSnapshot Repository => Item.Repository;
    public BranchState? Branch => Item.Branch;
    public WipKind Kind => Item.Kind;
    public string Detail => Item.Detail;
    public string WorktreePath => Item.Worktree?.Path ?? Repository.Worktrees.FirstOrDefault(worktree => worktree.Branch == Branch?.Name)?.Path ?? "—";
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
    public bool IsFlagged => Note?.IsFlagged ?? false;
    public bool CanFlag => Branch is not null;
    public string FlagGlyph => IsFlagged ? "⚑" : "⚐";
    public string FlagToolTip => Text[IsFlagged ? "RemoveFlag" : "AddFlag"];
    public string NoteText => Note?.Text ?? string.Empty;
    public string OpenFolderText => Text["OpenFolder"];
    public string ExcludeText => Text["ExcludeRepository"];
    public DateTimeOffset? LastCommitAt => Branch?.LastCommitAt;
    public string ActivityText =>
        IsStashed
            ? $"{Text["LatestStash"]}: {FormatDate(Item.Worktree?.LatestStashAt)} · {Text["DiskChanged"]}: {FormatDate(Repository.DirectoryModifiedAt)}"
            : $"{Text["LastCommit"]}: {FormatDate(LastCommitAt)} · {Text["DiskChanged"]}: {FormatDate(Repository.DirectoryModifiedAt)}";

    public string DiskSizeText => FormatSize(Repository.DiskSizeBytes);

    public string EvidenceText => Kind switch
    {
        WipKind.Dirty => $"{Item.Worktree?.Modified ?? 0} modified · {Item.Worktree?.Staged ?? 0} staged · {Item.Worktree?.Untracked ?? 0} untracked",
        WipKind.Stashed => $"{Item.Worktree?.StashCount ?? 0} stash(es)",
        WipKind.Unpushed => Branch?.Upstream is null ? "No upstream configured" : $"{Branch.Ahead} commit(s) ahead of {Branch.Upstream}",
        WipKind.StaleUnmerged => $"Last commit {FormatDate(LastCommitAt)} · not merged into base",
        WipKind.CleanCandidate => "Merged into a configured base branch",
        _ => Detail
    };

    public string Summary => $"{Repository.Repo.Name} — {ScopeName}: {Detail}";

    private static string FormatDate(DateTimeOffset? value) => value is { } date
        ? date.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture)
        : "—";

    private static string FormatSize(long? bytes) => bytes is not { } size
        ? "—"
        : size < 1024L * 1024L
            ? $"{size / 1024d:F1} KB"
            : size < 1024L * 1024L * 1024L
                ? $"{size / (1024d * 1024d):F1} MB"
                : $"{size / (1024d * 1024d * 1024d):F2} GB";
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
