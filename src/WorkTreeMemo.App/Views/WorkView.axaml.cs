using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace WorkTreeMemo.App.Views;

public partial class WorkView : UserControl
{
    public WorkView() => InitializeComponent();

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
}
