using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WorkTreeMemo.App.ViewModels;

namespace WorkTreeMemo.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void ChooseFolder(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { CanPickFolder: true } storageProvider) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose a scan root"
        });
        if (folders.Count > 0 && DataContext is MainViewModel viewModel)
            viewModel.NewRootPath = folders[0].Path.LocalPath;
    }
}
