using System.Windows.Media;

namespace MIXO6.Services;

/// <summary>
/// Resolves Persian-friendly UI fonts that are already installed for the current Windows user.
/// MIXO6 never installs fonts silently and has a reliable fallback when none are installed.
/// </summary>
public static class FontResolver
{
    private static readonly string[] PreferredFamilies =
    {
        "Vazirmatn", "Estedad", "IRANSansX", "Peyda", "Yekan Bakh",
        "Shabnam", "Sahel", "Noto Sans Arabic", "Tahoma", "Segoe UI"
    };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Installed = new(() =>
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            var source = family.Source?.Trim();
            if (!string.IsNullOrWhiteSpace(source)) result[source] = source;
            foreach (var localized in family.FamilyNames.Values)
            {
                var name = localized?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) result[name] = source ?? name;
            }
        }
        return result;
    });

    public static IReadOnlyList<string> GetAvailablePersianFonts()
    {
        var installed = Installed.Value;
        var list = new List<string>();
        foreach (var candidate in PreferredFamilies)
        {
            var match = installed.Keys.FirstOrDefault(key => key.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                match = installed.Keys.FirstOrDefault(key => key.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                var source = installed[match];
                if (!list.Contains(source, StringComparer.OrdinalIgnoreCase)) list.Add(source);
            }
        }
        return list;
    }

    public static string ResolveInstalled(string? preferred)
    {
        var installed = Installed.Value;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var name = installed.Keys.FirstOrDefault(key => key.Equals(preferred.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) name = installed.Keys.FirstOrDefault(key => key.Contains(preferred.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is not null) return installed[name];
        }

        foreach (var candidate in PreferredFamilies)
        {
            var name = installed.Keys.FirstOrDefault(key => key.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (name is null) name = installed.Keys.FirstOrDefault(key => key.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (name is not null) return installed[name];
        }
        return "Segoe UI";
    }
}
