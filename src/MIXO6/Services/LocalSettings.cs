using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MIXO6.Services;

public sealed class AppSettings
{
    // Public Supabase project settings. Publishable keys are intended for client apps;
    // never add sb_secret/service_role keys here.
    public const string DefaultSupabaseUrl = "https://tgltkobvufqazjrjckpe.supabase.co";
    public const string DefaultSupabasePublishableKey = "sb_publishable_J2TQ5vf56HKdvQiqIZqYbw_0vyyNQcO";

    public string ApiBaseUrl { get; set; } = DefaultSupabaseUrl;
    public string SupabasePublishableKey { get; set; } = DefaultSupabasePublishableKey;
    public string DisplayName { get; set; } = "Player";
    public string Avatar { get; set; } = "◈";
    public string MainGame { get; set; } = "CS2";
    public bool ShowWidget { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
}

public sealed class SessionData
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTimeOffset AccessExpiresAt { get; set; }
}

public static class LocalSettings
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MIXO6");
    private static readonly string SettingsPath = Path.Combine(Root, "settings.json");
    private static readonly string SessionPath = Path.Combine(Root, "session.bin");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static AppSettings Current { get; private set; } = new();
    public static string RootPath => Root;

    public static void Load()
    {
        Directory.CreateDirectory(Root);
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            else
                Current = new AppSettings();
        }
        catch { Current = new AppSettings(); }

        // Upgrade older installs with an empty backend configuration without overwriting
        // any non-empty user configuration.
        if (string.IsNullOrWhiteSpace(Current.ApiBaseUrl)) Current.ApiBaseUrl = AppSettings.DefaultSupabaseUrl;
        // The early MIXO6 drafts used a separate custom API ending in /api. Do not keep
        // that obsolete endpoint in a Supabase build after upgrading the app.
        else if (Uri.TryCreate(Current.ApiBaseUrl.Trim(), UriKind.Absolute, out var legacyUri) &&
                 legacyUri.AbsolutePath.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            Current.ApiBaseUrl = AppSettings.DefaultSupabaseUrl;
        if (string.IsNullOrWhiteSpace(Current.SupabasePublishableKey) ||
            Current.SupabasePublishableKey.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase) ||
            Current.SupabasePublishableKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            Current.SupabasePublishableKey = AppSettings.DefaultSupabasePublishableKey;
    }

    public static void SaveSettings()
    {
        Directory.CreateDirectory(Root);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temp, SettingsPath, true);
    }

    public static SessionData? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionPath)) return null;
            var encrypted = File.ReadAllBytes(SessionPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SessionData>(plain);
        }
        catch { return null; }
    }

    public static void SaveSession(SessionData session)
    {
        Directory.CreateDirectory(Root);
        var plain = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        var temp = SessionPath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, SessionPath, true);
    }

    public static void ClearSession()
    {
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
    }
}
