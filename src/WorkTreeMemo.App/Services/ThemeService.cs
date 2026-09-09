using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using WorkTreeMemo.Core.Models;

namespace WorkTreeMemo.App.Services;

/// <summary>
/// The only place that talks to Avalonia about colour schemes. Everything else
/// deals in <see cref="ThemePreference"/> and leaves the platform to this class.
/// </summary>
public static class ThemeService
{
    /// <summary>Reads the colour scheme the operating system is currently using.</summary>
    public static ThemePreference DetectOperatingSystemTheme() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            ? ThemePreference.Dark
            : ThemePreference.Light;

    /// <summary>Repaints the running application. Safe to call before the first window exists.</summary>
    public static void Apply(ThemePreference theme)
    {
        if (Application.Current is not { } application) return;
        application.RequestedThemeVariant = theme == ThemePreference.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
