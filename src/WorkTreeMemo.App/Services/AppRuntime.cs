using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WorkTreeMemo.App.ViewModels;
using WorkTreeMemo.App.Views;
using WorkTreeMemo.Core.Localization;
using WorkTreeMemo.Core.Scanning;

namespace WorkTreeMemo.App.Services;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly MainWindow _window;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly ScanCoordinator _coordinator;
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _periodicTask;
    private TrayIcon? _tray;

    public AppRuntime(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime, ScanCoordinator coordinator,
        MainViewModel viewModel)
    {
        _window = window;
        _lifetime = lifetime;
        _coordinator = coordinator;
        _viewModel = viewModel;
        _window.DataContext = _viewModel;
        _coordinator.SnapshotUpdated +=
            (_, snapshot) => Dispatcher.UIThread.Post(() => _viewModel.ApplySnapshot(snapshot));
    }

    public void Start()
    {
        _tray = new TrayIcon
        {
            ToolTipText = "WorkTreeMemo",
            Icon = new WindowIcon(
                new Bitmap(AssetLoader.Open(new Uri("avares://WorkTreeMemo/Assets/worktreememo-tray.png")))),
            Menu = new NativeMenu()
        };
        var text = new Localizer();
        var open = new NativeMenuItem(text["Open"]);
        open.Click += (_, _) => _window.ShowAndActivate();
        var reindex = new NativeMenuItem(text["Reindex"]);
        reindex.Click += async (_, _) => await _viewModel.ScanAsync(true);
        var quit = new NativeMenuItem(text["Quit"]);
        quit.Click += (_, _) => _lifetime.Shutdown();
        _tray.Menu!.Items.Add(open);
        _tray.Menu.Items.Add(reindex);
        _tray.Menu.Items.Add(new NativeMenuItemSeparator());
        _tray.Menu.Items.Add(quit);
        _ = StartScanningAsync();
    }

    private async Task StartScanningAsync()
    {
        await _viewModel.InitializeAsync();
        if (_viewModel.IsGitAvailable) _periodicTask = _coordinator.RunPeriodicAsync(_shutdown.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_periodicTask is not null)
            try
            {
                await _periodicTask;
            }
            catch (OperationCanceledException)
            {
            }

        _tray?.Dispose();
        _shutdown.Dispose();
        await _coordinator.DisposeAsync();
    }
}
