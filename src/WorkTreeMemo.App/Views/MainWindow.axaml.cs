using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WorkTreeMemo.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
