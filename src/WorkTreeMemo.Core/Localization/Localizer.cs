using System.Globalization;
using System.Resources;

namespace WorkTreeMemo.Core.Localization;

public sealed class Localizer
{
    private readonly ResourceManager _resources = new("WorkTreeMemo.Core.Properties.Resources",
        typeof(Localizer).Assembly);

    public string this[string key] => _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Switches the culture used for resource lookups. A culture name that the runtime
    /// cannot resolve is ignored rather than thrown: the configuration file is editable
    /// by hand, and a typo in it must not stop the application from starting.
    /// </summary>
    public static void UseCulture(string culture)
    {
        CultureInfo value;
        try
        {
            value = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            value = CultureInfo.InvariantCulture;
        }

        CultureInfo.DefaultThreadCurrentCulture = value;
        CultureInfo.DefaultThreadCurrentUICulture = value;
    }
}
