using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WorkTreeMemo.App.Composition;
using WorkTreeMemo.App.Services;
using WorkTreeMemo.App.Views;
using WorkTreeMemo.Core.Localization;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.App;

public partial class App : Application
{
    private AppRuntime? _runtime;
    private ServiceProvider? _services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _services = new ServiceCollection().AddWorkTreeMemo().BuildServiceProvider(validateScopes: true);
            ApplyStoredPreferences(_services.GetRequiredService<AppDataStore>());
            var window = new MainWindow();
            _runtime = ActivatorUtilities.CreateInstance<AppRuntime>(_services, window, desktop);
            desktop.MainWindow = window;
            desktop.Exit += async (_, _) =>
            {
                if (_runtime is not null) await _runtime.DisposeAsync();
                if (_services is not null) await _services.DisposeAsync();
            };
            _runtime.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Settles language and colour scheme before the first window is built, so nothing
    /// is painted in the wrong theme and then repainted. On a first launch there is no
    /// stored theme, so the operating system's own choice is adopted and written to the
    /// configuration file; from then on the file is what decides.
    /// </summary>
    private static void ApplyStoredPreferences(AppDataStore store)
    {
        // Off the UI thread on purpose: the store's continuations would otherwise queue
        // on the very dispatcher this method blocks.
        var configuration = Task.Run(() => store.LoadConfigurationAsync()).GetAwaiter().GetResult();
        Localizer.UseCulture(configuration.Culture);

        var theme = configuration.Theme ?? ThemeService.DetectOperatingSystemTheme();
        ThemeService.Apply(theme);
        if (configuration.Theme is not null) return;

        Task.Run(() => store.SaveConfigurationAsync(configuration with { Theme = theme })).GetAwaiter().GetResult();
    }
}
