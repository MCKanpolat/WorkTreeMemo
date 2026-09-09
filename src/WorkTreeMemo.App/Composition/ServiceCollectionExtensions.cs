using Microsoft.Extensions.DependencyInjection;
using WorkTreeMemo.App.Services;
using WorkTreeMemo.App.ViewModels;
using WorkTreeMemo.Core.Git;
using WorkTreeMemo.Core.Scanning;
using WorkTreeMemo.Core.Storage;

namespace WorkTreeMemo.App.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkTreeMemo(this IServiceCollection services)
    {
        services.AddSingleton<AppDataStore>();
        services.AddSingleton<RepoDiscovery>();
        services.AddSingleton<GitCliReader>();
        services.AddSingleton<GitInstallationChecker>();
        services.AddSingleton<ScanCoordinator>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ApplicationVersionProvider>();
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<GitHubReleaseUpdateChecker>();
        return services;
    }
}
