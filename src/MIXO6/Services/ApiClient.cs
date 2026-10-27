using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MIXO6.Models;

namespace MIXO6.Services;

/// <summary>
/// Supabase client implemented over the public Auth and PostgREST HTTP APIs.
/// Only a publishable/anon key belongs here. Never put service_role or sb_secret keys in a desktop app.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public SessionData? Session { get; private set; }
    public string BaseUrl { get; private set; } = "";
    public string PublishableKey { get; private set; } = "";

    public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) &&
        (u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
         (u.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
          (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))) &&
        PublishableKey.Length >= 16 && !PublishableKey.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase) &&
        !PublishableKey.Contains("service_role", StringComparison.OrdinalIgnoreCase);

    public bool IsSignedIn => Session is not null && !string.IsNullOrWhiteSpace(Session.AccessToken);

    public ApiClient(string baseUrl, string publishableKey)
    {
        // Migrate a saved base URL from the earlier custom-API build if it ended with /api.
        if (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl.TrimEnd('/')[..^4];
        SetBaseUrl(baseUrl);
        SetPublishableKey(publishableKey);
        Session = LocalSettings.LoadSession();
    }

    public void SetBaseUrl(string value)
    {
        value = (value ?? "").Trim().TrimEnd('/');
        if (value.Length > 400) throw new ArgumentException("نشانی پروژه Supabase بیش از حد طولانی است.");
        if (!string.IsNullOrEmpty(value))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var u) ||
                !(u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                  (u.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))))
                throw new ArgumentException("نشانی پروژه باید HTTPS باشد (به‌جز localhost برای تست).");
            if (!u.AbsolutePath.Equals("/", StringComparison.Ordinal) && !string.IsNullOrEmpty(u.AbsolutePath))
                throw new ArgumentException("فقط آدرس ریشه‌ی پروژه Supabase را وارد کن؛ /rest/v1 یا /auth/v1 اضافه نکن.");
            if (!string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
                throw new ArgumentException("آدرس پروژه نباید Query یا Fragment داشته باشد؛ فقط URL ریشه‌ی Supabase را وارد کن.");
        }
        BaseUrl = value;
    }

    public void SetPublishableKey(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 500) throw new ArgumentException("کلید Supabase بیش از حد طولانی است.");
        if (value.StartsWith("sb_secret_", StringComparison.OrdinalIgnoreCase) || value.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("کلید Secret/service_role برای برنامه‌ی دسکتاپ ممنوع است؛ فقط Publishable یا anon key را وارد کن.");
        PublishableKey = value;
    }

    private string Url(string relative) => BaseUrl + "/" + relative.TrimStart('/');

    private async Task<JsonDocument> RequestAsync(HttpMethod method, string path, object? body = null, bool auth = false, string? prefer = null)
    {
        if (!IsConfigured) throw new InvalidOperationException("Project URL و Publishable/anon key سوپربیس هنوز تنظیم نشده‌اند. از تنظیمات MIXO6 واردشان کن.");
        if (auth && !IsSignedIn) throw new InvalidOperationException("برای این کار ابتدا وارد حساب شو.");

        using var request = new HttpRequestMessage(method, Url(path));
        request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (auth && Session is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
        if (!string.IsNullOrWhiteSpace(prefer)) request.Headers.TryAddWithoutValidation("Prefer", prefer);
        if (body is not null) request.Content = JsonContent.Create(body, options: _json);

        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new SupabaseApiException((int)response.StatusCode, FriendlyError(response.StatusCode, content));
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content); }
        catch (JsonException) { throw new InvalidOperationException("پاسخ Supabase قالب JSON معتبر نداشت."); }
    }

    private static string FriendlyError(HttpStatusCode status, string content)
    {
        var raw = content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            foreach (var key in new[] { "message", "msg", "error_description", "error", "details", "hint" })
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var candidate = val.GetString();
                    if (!string.IsNullOrWhiteSpace(candidate)) { raw = candidate!; break; }
                }
            }
        }
        catch { }
        var lower = raw.ToLowerInvariant();
        if (raw.Contains("MIXO6_USERNAME_TAKEN", StringComparison.OrdinalIgnoreCase)) return "این نام کاربری قبلاً گرفته شده است؛ نام دیگری انتخاب کن.";
        if (raw.Contains("MIXO6_USERNAME_INVALID", StringComparison.OrdinalIgnoreCase)) return "نام کاربری باید ۳ تا ۲۰ کاراکتر انگلیسی، عدد یا زیرخط باشد.";
        if (raw.Contains("ACTIVE_LOBBY_EXISTS", StringComparison.OrdinalIgnoreCase)) return "یک لابی باز داری؛ اول آن را ببند یا منتظر بمان تا تکمیل شود.";
        if (raw.Contains("LOBBY_FULL", StringComparison.OrdinalIgnoreCase)) return "این لابی پر شده است.";
        if (raw.Contains("LOBBY_NOT_FOUND_OR_NOT_OWNER", StringComparison.OrdinalIgnoreCase)) return "لابی پیدا نشد یا سازنده‌ی آن نیستی.";
        if (raw.Contains("LOBBY_NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return "این لابی دیگر وجود ندارد.";
        if (raw.Contains("LOBBY_CLOSED", StringComparison.OrdinalIgnoreCase)) return "این لابی بسته شده است.";
        if (raw.Contains("INVALID_GAME", StringComparison.OrdinalIgnoreCase)) return "نام بازی را وارد کن (حداکثر ۶۰ کاراکتر).";
        if (raw.Contains("INVALID_SERVER_ADDRESS", StringComparison.OrdinalIgnoreCase)) return "آدرس یا لینک سرور بازی را درست وارد کن.";
        if (raw.Contains("INVALID_SERVER_LABEL", StringComparison.OrdinalIgnoreCase)) return "عنوان سرور باید بین ۱ تا ۸۰ کاراکتر باشد.";
        if (raw.Contains("DESCRIPTION_TOO_LONG", StringComparison.OrdinalIgnoreCase)) return "توضیحات لابی حداکثر ۵۰۰ کاراکتر است.";
        if (lower.Contains("invalid login credentials")) return "ایمیل یا رمز عبور اشتباه است.";
        if (lower.Contains("email not confirmed")) return "ابتدا ایمیل خودت را تأیید کن.";
        if (lower.Contains("user already registered")) return "این ایمیل قبلاً ثبت‌نام کرده است.";
        if (lower.Contains("username") && (lower.Contains("taken") || lower.Contains("duplicate") || lower.Contains("unique"))) return "این نام کاربری قبلاً استفاده شده است.";
        if (lower.Contains("database error saving new user")) return "ثبت‌نام انجام نشد؛ ممکن است نام کاربری تکراری باشد. نام دیگری انتخاب کن و دوباره تلاش کن.";
        if (lower.Contains("over_email_send_rate_limit") || lower.Contains("email rate limit")) return "محدودیت ارسال ایمیل Supabase فعال شده است؛ SMTP اختصاصی را تنظیم کن یا کمی بعد تلاش کن.";
        if ((int)status == 429) return "درخواست‌ها بیش از حد بوده‌اند؛ کمی بعد دوباره تلاش کن.";
        if ((int)status is 401 or 403) return "نشست یا کلید نامعتبر است؛ تنظیمات اتصال Supabase و وضعیت ورود را بررسی کن.";
        return $"Supabase پاسخ {(int)status} داد: {raw}";
    }

    private async Task<JsonDocument> RequestAuthenticatedAsync(HttpMethod method, string path, object? body = null, string? prefer = null)
    {
        await EnsureSessionFreshAsync().ConfigureAwait(false);
        return await RequestAsync(method, path, body, auth: true, prefer: prefer).ConfigureAwait(false);
    }

    private async Task<bool> UsernameExistsAsync(string username)
    {
        using var doc = await RequestAsync(HttpMethod.Get, "rest/v1/profiles?select=username&username=eq." + Uri.EscapeDataString(username.ToLowerInvariant()) + "&limit=1").ConfigureAwait(false);
        return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
    }

    public async Task RegisterAsync(string username, string email, string password)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        email = (email ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
            throw new InvalidOperationException("نام کاربری باید ۳ تا ۲۰ کاراکتر انگلیسی، عدد یا زیرخط باشد.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$"))
            throw new InvalidOperationException("ایمیل معتبر وارد کن.");
        if (password.Length < 10 || password.Length > 128)
            throw new InvalidOperationException("رمز عبور باید بین ۱۰ تا ۱۲۸ کاراکتر باشد.");
        if (await UsernameExistsAsync(username).ConfigureAwait(false))
            throw new InvalidOperationException("این نام کاربری قبلاً گرفته شده است؛ نام دیگری انتخاب کن.");

        using var doc = await RequestAsync(HttpMethod.Post, "auth/v1/signup", new
        {
            email,
            password,
            data = new { username, avatar = "◈", main_game = "CS2" }
        }).ConfigureAwait(false);
        // The hosted project must have Email confirmations enabled. No session is stored until the OTP is verified.
    }

    public async Task VerifyEmailAsync(string email, string code)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        code = (code ?? "").Trim().Replace(" ", "");
        if (string.IsNullOrWhiteSpace(email) || code.Length < 6 || code.Length > 20)
            throw new InvalidOperationException("ایمیل و کد تأیید داخل پیام ایمیل را وارد کن.");
        using var doc = await RequestAsync(HttpMethod.Post, "auth/v1/verify", new { type = "signup", token = code, email }).ConfigureAwait(false);
        var tokens = doc.RootElement.Deserialize<SupabaseAuthResponse>(_json) ?? throw new InvalidOperationException("پاسخ تأیید ایمیل ناقص بود.");
        if (string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken))
            throw new InvalidOperationException("تأیید ایمیل انجام شد، اما نشست ورود دریافت نشد. از بخش ورود با ایمیل و رمز استفاده کن.");
        SaveTokens(tokens);
    }

    public async Task ResendVerificationAsync(string email)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        using var doc = await RequestAsync(HttpMethod.Post, "auth/v1/resend", new { type = "signup", email }).ConfigureAwait(false);
    }

    public async Task LoginAsync(string identifier, string password)
    {
        identifier = (identifier ?? "").Trim();
        if (string.IsNullOrWhiteSpace(identifier)) throw new InvalidOperationException("ایمیل یا نام کاربری را وارد کن.");
        if (password is null || password.Length < 1 || password.Length > 128) throw new InvalidOperationException("رمز عبور معتبر وارد کن.");

        JsonDocument response;
        if (identifier.Contains('@'))
        {
            var email = identifier.ToLowerInvariant();
            response = await RequestAsync(HttpMethod.Post, "auth/v1/token?grant_type=password", new { email, password }).ConfigureAwait(false);
        }
        else
        {
            var username = identifier.ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
                throw new InvalidOperationException("ایمیل معتبر یا نام کاربری ۳ تا ۲۰ حرفی انگلیسی/عدد/_ وارد کن.");
            // The Edge Function looks up the email server-side without exposing it publicly.
            response = await RequestAsync(HttpMethod.Post, "functions/v1/mixo6-login", new { username, password }).ConfigureAwait(false);
        }
        using (response)
        {
            var tokens = response.RootElement.Deserialize<SupabaseAuthResponse>(_json) ?? throw new InvalidOperationException("پاسخ ورود ناقص بود.");
            SaveTokens(tokens);
        }
    }

    private void SaveTokens(SupabaseAuthResponse tokens)
    {
        var old = Session;
        var user = tokens.User;
        var username = GetMetadata(user, "username");
        var id = user?.Id;
        if (string.IsNullOrWhiteSpace(id)) id = old?.UserId;
        if (string.IsNullOrWhiteSpace(username)) username = old?.Username;
        if (string.IsNullOrWhiteSpace(username)) username = user?.Email?.Split('@').FirstOrDefault() ?? "Player";
        if (string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken))
            throw new InvalidOperationException("Supabase نشست معتبر برنگرداند.");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("شناسه‌ی کاربر از Supabase دریافت نشد؛ دوباره وارد شو.");

        Session = new SessionData
        {
            UserId = id ?? "",
            Email = user?.Email ?? old?.Email ?? "",
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            Username = username,
            AccessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(tokens.ExpiresIn, 60, 86400))
        };
        LocalSettings.SaveSession(Session);
        LocalSettings.Current.DisplayName = username;
        LocalSettings.Current.Avatar = GetMetadata(user, "avatar") is string avatar && !string.IsNullOrWhiteSpace(avatar) ? avatar : LocalSettings.Current.Avatar;
        LocalSettings.Current.MainGame = GetMetadata(user, "main_game") is string game && !string.IsNullOrWhiteSpace(game) ? game : LocalSettings.Current.MainGame;
        LocalSettings.SaveSettings();
    }

    private static string? GetMetadata(SupabaseAuthUser? user, string key)
    {
        if (user?.UserMetadata is null || !user.UserMetadata.TryGetValue(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : value.ToString();
    }

    public async Task<bool> RestoreSessionAsync()
    {
        if (Session is null || string.IsNullOrWhiteSpace(Session.RefreshToken) || !IsConfigured) return false;
        // Sessions from the earlier custom API do not contain a Supabase user id; discard those legacy tokens.
        if (string.IsNullOrWhiteSpace(Session.UserId)) { SignOutLocal(); return false; }
        if (Session.AccessExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return true;
        try
        {
            using var doc = await RequestAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", new { refresh_token = Session.RefreshToken }).ConfigureAwait(false);
            var tokens = doc.RootElement.Deserialize<SupabaseAuthResponse>(_json);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken)) return false;
            SaveTokens(tokens);
            return true;
        }
        catch (SupabaseApiException ex) when (ex.StatusCode is 400 or 401)
        {
            SignOutLocal();
            return false;
        }
        catch { return false; }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (IsSignedIn && IsConfigured)
            {
                await EnsureSessionFreshAsync().ConfigureAwait(false);
                using var _ = await RequestAsync(HttpMethod.Post, "auth/v1/logout", auth: true).ConfigureAwait(false);
            }
        }
        catch { }
        SignOutLocal();
    }

    public void SignOutLocal() { Session = null; LocalSettings.ClearSession(); }

    private async Task EnsureSessionFreshAsync()
    {
        if (Session is null) throw new InvalidOperationException("برای این کار ابتدا وارد حساب شو.");
        if (Session.AccessExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return;
        if (string.IsNullOrWhiteSpace(Session.RefreshToken)) throw new InvalidOperationException("نشست منقضی شده؛ دوباره وارد شو.");
        try
        {
            using var doc = await RequestAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", new { refresh_token = Session.RefreshToken }).ConfigureAwait(false);
            var tokens = doc.RootElement.Deserialize<SupabaseAuthResponse>(_json);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken)) throw new InvalidOperationException("تمدید نشست ناموفق بود؛ دوباره وارد شو.");
            SaveTokens(tokens);
        }
        catch (SupabaseApiException ex) when (ex.StatusCode is 400 or 401)
        {
            SignOutLocal();
            throw new InvalidOperationException("نشست منقضی شده است؛ دوباره با ایمیل و رمز وارد شو.");
        }
    }

    public async Task HeartbeatAsync()
    {
        using var _ = await RequestAuthenticatedAsync(HttpMethod.Post, "rest/v1/rpc/mixo6_heartbeat", new { }).ConfigureAwait(false);
    }

    public async Task<ApiUser> GetMeAsync()
    {
        if (Session is null || string.IsNullOrWhiteSpace(Session.UserId)) throw new InvalidOperationException("اطلاعات نشست موجود نیست؛ دوباره وارد شو.");
        using var authDoc = await RequestAuthenticatedAsync(HttpMethod.Get, "auth/v1/user").ConfigureAwait(false);
        var authUser = authDoc.RootElement.Deserialize<SupabaseAuthUser>(_json);
        using var profileDoc = await RequestAuthenticatedAsync(HttpMethod.Get,
            "rest/v1/profiles?select=id,username,avatar,main_game,last_seen_at&id=eq." + Uri.EscapeDataString(Session.UserId) + "&limit=1").ConfigureAwait(false);
        var profile = profileDoc.RootElement.Deserialize<List<ProfileRow>>(_json)?.FirstOrDefault();
        if (profile is null) throw new InvalidOperationException("پروفایل پیدا نشد؛ احتمالاً SQL راه‌اندازی Supabase هنوز اجرا نشده است.");
        Session.Username = profile.Username;
        Session.Email = authUser?.Email ?? Session.Email;
        LocalSettings.SaveSession(Session);
        return ToApiUser(profile, authUser?.Email ?? Session.Email, authUser?.EmailConfirmedAt is not null);
    }

    public async Task<ApiUser> UpdateProfileAsync(string avatar, string mainGame)
    {
        if (Session is null || string.IsNullOrWhiteSpace(Session.UserId)) throw new InvalidOperationException("ابتدا وارد حساب شو.");
        if (string.IsNullOrWhiteSpace(mainGame) || mainGame.Length > 60) throw new InvalidOperationException("نام بازی اصلی باید حداکثر ۶۰ کاراکتر باشد.");
        using var doc = await RequestAuthenticatedAsync(HttpMethod.Patch,
            "rest/v1/profiles?id=eq." + Uri.EscapeDataString(Session.UserId) + "&select=id,username,avatar,main_game,last_seen_at",
            new { avatar, main_game = mainGame.Trim() }, "return=representation").ConfigureAwait(false);
        var profile = doc.RootElement.Deserialize<List<ProfileRow>>(_json)?.FirstOrDefault();
        if (profile is null) throw new InvalidOperationException("ذخیره‌ی پروفایل انجام نشد.");
        LocalSettings.Current.Avatar = profile.Avatar;
        LocalSettings.Current.MainGame = profile.MainGame;
        LocalSettings.SaveSettings();
        return ToApiUser(profile, Session.Email, true);
    }

    public async Task<List<LobbyDto>> GetLobbiesAsync(string? game = null)
    {
        var path = "rest/v1/lobby_cards?select=id,game,server_label,description,status,creator_username,creator_avatar,creator_online,member_count,created_at&order=created_at.desc";
        if (!string.IsNullOrWhiteSpace(game)) path += "&game=ilike." + Uri.EscapeDataString("%" + game.Trim() + "%");
        using var doc = await RequestAsync(HttpMethod.Get, path).ConfigureAwait(false);
        return doc.RootElement.Deserialize<List<LobbyDto>>(_json) ?? new List<LobbyDto>();
    }

    public async Task<LobbyDto> CreateLobbyAsync(CreateLobbyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var game = (request.Game ?? "").Trim();
        var serverAddress = (request.ServerAddress ?? "").Trim();
        var serverLabel = string.IsNullOrWhiteSpace(request.ServerLabel) ? "Custom" : request.ServerLabel.Trim();
        var description = (request.Description ?? "").Trim();

        if (game.Length is < 1 or > 60) throw new InvalidOperationException("نام بازی باید بین ۱ تا ۶۰ کاراکتر باشد.");
        if (serverAddress.Length is < 1 or > 300) throw new InvalidOperationException("آدرس سرور باید بین ۱ تا ۳۰۰ کاراکتر باشد.");
        if (serverLabel.Length is < 1 or > 80) throw new InvalidOperationException("عنوان سرور باید بین ۱ تا ۸۰ کاراکتر باشد.");
        if (description.Length > 500) throw new InvalidOperationException("توضیحات لابی حداکثر ۵۰۰ کاراکتر است.");

        using var doc = await RequestAuthenticatedAsync(HttpMethod.Post, "rest/v1/rpc/mixo6_create_lobby", new
        {
            p_game = game,
            p_server_address = serverAddress,
            p_server_label = serverLabel,
            p_description = description
        }).ConfigureAwait(false);

        long lobbyId = doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.TryGetInt64(out var createdId)
            ? createdId
            : 0;
        return new LobbyDto
        {
            Id = lobbyId,
            Game = game,
            ServerLabel = serverLabel,
            Description = description,
            Status = "open",
            CreatorUsername = Session?.Username ?? "",
            CreatorAvatar = LocalSettings.Current.Avatar,
            MemberCount = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<JoinLobbyResult> JoinLobbyAsync(long id)
    {
        using var doc = await RequestAuthenticatedAsync(HttpMethod.Post, "rest/v1/rpc/mixo6_join_lobby", new { p_lobby_id = id }).ConfigureAwait(false);
        var result = doc.RootElement;
        return new JoinLobbyResult
        {
            LobbyId = result.TryGetProperty("lobbyId", out var lid) && lid.TryGetInt64(out var n) ? n : id,
            ServerAddress = GetJsonString(result, "serverAddress"),
            CreatorUsername = GetJsonString(result, "creatorUsername"),
            Game = GetJsonString(result, "game"),
            Description = GetJsonString(result, "description")
        };
    }

    public async Task CloseLobbyAsync(long id)
    {
        using var _ = await RequestAuthenticatedAsync(HttpMethod.Post, "rest/v1/rpc/mixo6_close_lobby", new { p_lobby_id = id }).ConfigureAwait(false);
    }

    public async Task<PublicProfile> GetPublicProfileAsync(string username)
    {
        using var doc = await RequestAsync(HttpMethod.Get,
            "rest/v1/profiles?select=username,avatar,main_game,last_seen_at&username=eq." + Uri.EscapeDataString((username ?? "").Trim().ToLowerInvariant()) + "&limit=1").ConfigureAwait(false);
        var profile = doc.RootElement.Deserialize<List<ProfileRow>>(_json)?.FirstOrDefault();
        if (profile is null) throw new InvalidOperationException("پروفایل عمومی پیدا نشد.");
        return new PublicProfile
        {
            Username = profile.Username,
            Avatar = profile.Avatar,
            MainGame = profile.MainGame,
            LastSeenAt = profile.LastSeenAt,
            Online = profile.LastSeenAt is DateTimeOffset seen && seen >= DateTimeOffset.UtcNow.AddMinutes(-5)
        };
    }

    private static ApiUser ToApiUser(ProfileRow row, string email, bool verified) => new()
    {
        Id = row.Id,
        Username = row.Username,
        Email = email,
        Avatar = row.Avatar,
        MainGame = row.MainGame,
        IsVerified = verified
    };

    private static string GetJsonString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private sealed class SupabaseApiException(int statusCode, string message) : InvalidOperationException(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed class SupabaseAuthResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; } = 3600;
        [JsonPropertyName("user")] public SupabaseAuthUser? User { get; set; }
    }

    private sealed class SupabaseAuthUser
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("email_confirmed_at")] public DateTimeOffset? EmailConfirmedAt { get; set; }
        [JsonPropertyName("user_metadata")] public Dictionary<string, JsonElement>? UserMetadata { get; set; }
    }

    private sealed class ProfileRow
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("avatar")] public string Avatar { get; set; } = "◈";
        [JsonPropertyName("main_game")] public string MainGame { get; set; } = "CS2";
        [JsonPropertyName("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
    }

    public void Dispose() => _http.Dispose();
}
