using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using WorkTreeMemo.App.ViewModels;

namespace WorkTreeMemo.App.Views;

public partial class WorkView : UserControl
{
    public WorkView() => InitializeComponent();

    public void SetAllExpanded(bool isExpanded)
    {
        for (var index = 0; index < WorkTree.ItemCount; index++)
            if (WorkTree.ContainerFromIndex(index) is TreeViewItem item) item.IsExpanded = isExpanded;
    }

    private static void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } || !Directory.Exists(path)) return;

        var executable = OperatingSystem.IsWindows()
            ? "explorer.exe"
            : OperatingSystem.IsMacOS()
                ? "open"
                : "xdg-open";
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add(path);
        Process.Start(start);
    }

    private async void ExcludeRepository(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } || DataContext is not MainViewModel viewModel) return;
        await viewModel.ExcludeRepositoryAsync(path);
    }

    private async void ToggleFavorite(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RepoGroup group } || DataContext is not MainViewModel viewModel) return;
        await viewModel.ToggleFavoriteAsync(group);
    }

    private async void ToggleFlag(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WipRow row } || DataContext is not MainViewModel viewModel) return;
        await viewModel.ToggleFlagAsync(row);
    }
}
