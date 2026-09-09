using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WorkTreeMemo.Core.Models;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.App.Views;

public partial class MainWindow : Window
{
    private const int MinimumVisibleWidth = 240;
    private const int MinimumVisibleHeight = 180;
    private const double DefaultScreenCoverage = 0.82;
    private const double WideScreenAspectRatio = 2.0;
    private const double WideScreenWidthCoverage = 0.70;
    private const int WideScreenMaximumDefaultWidth = 1800;
    private AppDataStore? _store;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void EnablePlacementPersistence(AppDataStore store)
    {
        _store = store;
        Opened += async (_, _) => await RestorePlacementAsync();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public async Task SavePlacementAsync()
    {
        if (_store is null) return;
        if (WindowState != WindowState.Normal) return;

        var configuration = await _store.LoadConfigurationAsync();
        var placement = new WindowPlacement(Position.X, Position.Y, Width, Height);
        await _store.SaveConfigurationAsync(configuration with { LastWindowPlacement = placement });
    }

    private async Task RestorePlacementAsync()
    {
        if (_store is null) return;
        var placement = (await _store.LoadConfigurationAsync()).LastWindowPlacement;
        if (placement is not null && TryRestorePlacement(placement)) return;
        CenterOnAvailableScreen();
    }

    private bool TryRestorePlacement(WindowPlacement placement)
    {
        var screen = Screens.All
            .Select(candidate => new
            {
                Screen = candidate,
                VisibleArea = IntersectionArea(candidate.WorkingArea, placement)
            })
            .OrderByDescending(candidate => candidate.VisibleArea)
            .FirstOrDefault();
        if (screen is null || screen.VisibleArea < MinimumVisibleWidth * MinimumVisibleHeight) return false;

        var area = screen.Screen.WorkingArea;
        Width = Math.Clamp(placement.Width, MinWidth, area.Width);
        Height = Math.Clamp(placement.Height, MinHeight, area.Height);
        Position = new PixelPoint(
            Math.Clamp(placement.X, area.X - (int)Width + MinimumVisibleWidth, area.Right - MinimumVisibleWidth),
            Math.Clamp(placement.Y, area.Y - (int)Height + MinimumVisibleHeight, area.Bottom - MinimumVisibleHeight));
        return true;
    }

    private void CenterOnAvailableScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;

        var area = screen.WorkingArea;
        var widthCoverage = area.Width / (double)area.Height >= WideScreenAspectRatio
            ? WideScreenWidthCoverage
            : DefaultScreenCoverage;
        var targetWidth = Math.Round(area.Width * widthCoverage);
        if (area.Width / (double)area.Height >= WideScreenAspectRatio)
            targetWidth = Math.Min(targetWidth, WideScreenMaximumDefaultWidth);

        Width = Math.Min(area.Width, Math.Max(MinWidth, targetWidth));
        Height = Math.Min(area.Height, Math.Max(MinHeight, Math.Round(area.Height * DefaultScreenCoverage)));
        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - (int)Width) / 2),
            area.Y + Math.Max(0, (area.Height - (int)Height) / 2));
    }

    private static int IntersectionArea(PixelRect bounds, WindowPlacement placement)
    {
        var width = Math.Max(0, Math.Min(bounds.Right, placement.X + (int)placement.Width) - Math.Max(bounds.X, placement.X));
        var height = Math.Max(0, Math.Min(bounds.Bottom, placement.Y + (int)placement.Height) - Math.Max(bounds.Y, placement.Y));
        return width * height;
    }

    private void CollapseAll(object? sender, RoutedEventArgs e) => WorkQueueView.SetAllExpanded(false);

    private void ExpandAll(object? sender, RoutedEventArgs e) => WorkQueueView.SetAllExpanded(true);
}
