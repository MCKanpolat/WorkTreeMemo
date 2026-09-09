using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using WorkTreeMemo.App.Composition;
using WorkTreeMemo.Core.Localization;

namespace WorkTreeMemo.App;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (CliApplication.IsCliInvocation(args)) return await CliApplication.RunAsync(args);
        Localizer.UseCulture("en-US");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args
            .Where(argument => !argument.Equals("--gui", StringComparison.OrdinalIgnoreCase)).ToArray());
        return 0;
    }

    public static ServiceProvider CreateServiceProvider() =>
        new ServiceCollection().AddWorkTreeMemo().BuildServiceProvider(validateScopes: true);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
