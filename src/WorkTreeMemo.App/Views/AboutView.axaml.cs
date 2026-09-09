using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace WorkTreeMemo.App.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    private static void OpenWebPage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
