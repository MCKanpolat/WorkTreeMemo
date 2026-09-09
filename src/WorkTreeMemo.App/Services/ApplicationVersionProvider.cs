using System.Reflection;

namespace WorkTreeMemo.App.Services;

public sealed class ApplicationVersionProvider
{
    public string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationVersionProvider).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0-dev").Split('+')[0];
    }
}
