using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MIXO6.Models;
using MIXO6.Services;

namespace MIXO6;

public partial class MainWindow : Window
{
    private readonly HardwareMonitorService _hardware = new();
    private readonly DispatcherTimer _sensorTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly DispatcherTimer _presenceTimer = new() { Interval = TimeSpan.FromSeconds(90) };
    private ApiClient _api = null!;
    private FloatingWidget? _widget;
    private int _sensorBusy;
    private string _joinedServerAddress = "";

    private static readonly (string Name, string Subtitle)[] PageNames =
    {
        ("مرکز کنترل", "کنترل سخت‌افزار، شبکه و بازی‌ها؛ در یک پنل"),
        ("DNS Studio", "پروفایل‌های DNS، تست Ping و ابزارهای شبکه"),
        ("تورنومنت و لابی", "ساخت لابی بازی و پیدا کردن حریف"),
        ("حساب کاربری", "حساب MIXO6، پروفایل و ورود ماندگار"),
        ("Origin Hub", "میانبر سایت‌های اختصاصی"),
        ("فروشگاه", "فروشگاه MIXO6"),
        ("تنظیمات", "اتصال سرور و شخصی‌سازی رفتار برنامه")
    };

    public MainWindow()
    {
        LocalSettings.Load();
        InitializeComponent();
        _api = new ApiClient(LocalSettings.Current.ApiBaseUrl, LocalSettings.Current.SupabasePublishableKey);
        ApiUrlBox.Text = LocalSettings.Current.ApiBaseUrl;
        SupabasePublishableKeyBox.Text = LocalSettings.Current.SupabasePublishableKey;
        ProfileMainGameBox.Text = LocalSettings.Current.MainGame;
        ProfileAvatarCombo.SelectedIndex = AvatarIndex(LocalSettings.Current.Avatar);
        ShowWidgetCheck.IsChecked = LocalSettings.Current.ShowWidget;
        StartupCheck.IsChecked = LocalSettings.Current.StartWithWindows;
        SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        NetworkInterfaceCombo.SelectionChanged += (_, _) => RefreshDnsStatus();
        _sensorTimer.Tick += async (_, _) => await RefreshTemperaturesAsync();
        _presenceTimer.Tick += async (_, _) =>
        {
            if (_api.IsConfigured && _api.IsSignedIn)
            {
                try { await _api.HeartbeatAsync(); } catch { /* Presence resumes on the next successful interval. */ }
            }
        };
        _sensorTimer.Start();
        _presenceTimer.Start();
        RefreshConnectionBadge();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateNetworkInterfaces();
        RefreshDnsStatus();
        if (LocalSettings.Current.ShowWidget) EnsureWidgetVisible();
        await RefreshTemperaturesAsync();
        if (_api.IsConfigured && _api.Session is not null)
        {
            var restored = await _api.RestoreSessionAsync();
            if (restored)
            {
                try { var me = await _api.GetMeAsync(); ApplyRemoteUser(me); }
                catch { UpdateAuthState("نشست ذخیره شده است، اما سرور پاسخ نداد.", false); }
            }
        }
        RefreshConnectionBadge();
        if (_api.IsConfigured) await RefreshLobbiesCoreAsync(showStatus: false);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _sensorTimer.Stop();
        _presenceTimer.Stop();
        _hardware.Dispose();
        _api.Dispose();
        try { _widget?.Close(); } catch { }
        LocalSettings.SaveSettings();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return;
            current = GetUiParent(current);
        }
        if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }


    private static DependencyObject? GetUiParent(DependencyObject current)
    {
        if (current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D)
            return System.Windows.Media.VisualTreeHelper.GetParent(current);
        if (current is ContentElement contentElement)
            return ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(current);
        return LogicalTreeHelper.GetParent(current);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SelectPage(int index)
    {
        UIElement[] panels = { DashboardPanel, DnsPanel, TournamentPanel, AccountPanel, SitesPanel, StorePanel, SettingsPanel };
        var navs = new[] { NavDashboard, NavDns, NavTournament, NavAccount, NavSites, NavStore, NavSettings };
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            navs[i].Tag = i == index ? "Selected" : null;
        }
        PageTitleText.Text = PageNames[index].Name;
        PageSubtitleText.Text = PageNames[index].Subtitle;
        if (index == 1) RefreshDnsStatus();
        if (index == 2 && _api.IsConfigured) _ = RefreshLobbiesCoreAsync(showStatus: false);
        if (index == 3) ReflectAccountState();
        if (index == 6) { ApiUrlBox.Text = LocalSettings.Current.ApiBaseUrl; SupabasePublishableKeyBox.Text = LocalSettings.Current.SupabasePublishableKey; RefreshConnectionBadge(); }
        var panel = panels[index];
        panel.Opacity = 0;
        var animation = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230));
        panel.BeginAnimation(OpacityProperty, animation);
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SelectPage(0);
    private void NavDns_Click(object sender, RoutedEventArgs e) => SelectPage(1);
    private void NavTournament_Click(object sender, RoutedEventArgs e) => SelectPage(2);
    private void NavAccount_Click(object sender, RoutedEventArgs e) => SelectPage(3);
    private void NavSites_Click(object sender, RoutedEventArgs e) => SelectPage(4);
    private void NavStore_Click(object sender, RoutedEventArgs e) => SelectPage(5);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SelectPage(6);
    private void OpenDnsPage_Click(object sender, RoutedEventArgs e) => SelectPage(1);
    private void OpenTournamentPage_Click(object sender, RoutedEventArgs e) => SelectPage(2);
    private void OpenSitesPage_Click(object sender, RoutedEventArgs e) => SelectPage(4);

    private async Task RefreshTemperaturesAsync()
    {
        if (Interlocked.Exchange(ref _sensorBusy, 1) == 1) return;
        try
        {
            var snapshot = await Task.Run(() => _hardware.Read());
            CpuTempText.Text = snapshot.CpuCelsius is double c ? $"{c:0}°C" : "--°C";
            GpuTempText.Text = snapshot.GpuCelsius is double g ? $"{g:0}°C" : "--°C";
            CpuTempBar.Value = snapshot.CpuCelsius is double cv ? Math.Clamp(cv, 0, 110) : 0;
            GpuTempBar.Value = snapshot.GpuCelsius is double gv ? Math.Clamp(gv, 0, 110) : 0;
            CpuSensorNameText.Text = snapshot.CpuCelsius is null ? "سنسور CPU در دسترس نیست" : Shorten(snapshot.CpuName, 54);
            GpuSensorNameText.Text = snapshot.GpuCelsius is null ? "سنسور GPU در دسترس نیست" : Shorten(snapshot.GpuName, 54);
            CpuTempCaption.Text = TempCaption(snapshot.CpuCelsius);
            GpuTempCaption.Text = TempCaption(snapshot.GpuCelsius);
            SensorStatusText.Text = snapshot.Status;
            _widget?.UpdateTemps(snapshot.CpuCelsius, snapshot.GpuCelsius);
        }
        catch (Exception ex) { SensorStatusText.Text = "خواندن سنسورها ناموفق بود: " + ex.Message; }
        finally { Volatile.Write(ref _sensorBusy, 0); }
    }

    private static string TempCaption(double? value) => value is null ? "بدون داده‌ی سنسور" : value >= 90 ? "دمای بالا؛ بررسی خنک‌کننده" : value >= 80 ? "گرم" : value >= 60 ? "در حال بارگذاری" : "محدوده‌ی معمول";
    private static string Shorten(string value, int count) => value.Length <= count ? value : value[..(count - 1)] + "…";

    private void PopulateNetworkInterfaces()
    {
        var previous = GetSelectedInterfaceName();
        NetworkInterfaceCombo.Items.Clear();
        foreach (var nic in DnsService.GetInterfaces())
        {
            NetworkInterfaceCombo.Items.Add(new ComboBoxItem { Content = $"{nic.Name}  ·  {nic.NetworkInterfaceType}", Tag = nic.Name });
        }
        var item = NetworkInterfaceCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), previous, StringComparison.OrdinalIgnoreCase));
        NetworkInterfaceCombo.SelectedItem = item ?? NetworkInterfaceCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        if (NetworkInterfaceCombo.SelectedItem is null) SelectedDnsStatusText.Text = "هیچ کارت شبکه‌ی فعال پیدا نشد.";
    }

    private string? GetSelectedInterfaceName() => (NetworkInterfaceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private void RefreshDnsStatus()
    {
        var selectedName = GetSelectedInterfaceName();
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            SelectedDnsStatusText.Text = "DNS فعلی: کارت شبکه‌ی فعال انتخاب نشده.";
            DashboardInterfaceText.Text = "کارت شبکه‌ی فعال پیدا نشد";
            DashboardDnsText.Text = "DNS: —";
            return;
        }
        var nic = DnsService.GetInterfaces().FirstOrDefault(n => string.Equals(n.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (nic is null) return;
        var dns = DnsService.GetDns(nic);
        var dnsText = dns.Count == 0 ? "از DHCP / خودکار" : string.Join("  ·  ", dns);
        SelectedDnsStatusText.Text = $"DNS فعلی: {dnsText}";
        DashboardInterfaceText.Text = $"{nic.Name} · {nic.NetworkInterfaceType} · {nic.OperationalStatus}";
        DashboardDnsText.Text = "DNS: " + dnsText;
    }

    private void PickDns(string primary, string secondary)
    {
        PrimaryDnsBox.Text = primary;
        SecondaryDnsBox.Text = secondary;
        DnsActionStatusText.Text = "پروفایل انتخاب شد؛ هنوز اعمال نشده است. برای تغییر واقعی روی ویندوز، «اعمال» را بزن.";
    }
    private void UseCs2Dns_Click(object sender, RoutedEventArgs e) => PickDns("8.8.8.8", "8.8.4.4");
    private void UseMinecraftDns_Click(object sender, RoutedEventArgs e) => PickDns("78.157.42.101", "78.157.42.100");
    private void UseOriginDns_Click(object sender, RoutedEventArgs e) => PickDns("194.146.68.68", "194.146.68.66");
    private void UseOriginDnsB_Click(object sender, RoutedEventArgs e) => PickDns("78.157.42.100", "78.157.42.10");

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        var name = GetSelectedInterfaceName();
        if (string.IsNullOrWhiteSpace(name)) { DnsActionStatusText.Text = "اول یک کارت شبکه‌ی فعال انتخاب کن."; return; }
        var servers = new[] { PrimaryDnsBox.Text.Trim(), SecondaryDnsBox.Text.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        DnsActionStatusText.Text = "درخواست دسترسی ویندوز… اگر پنجره‌ی UAC باز شد، فقط در صورت تأیید خودت ادامه بده.";
        try
        {
            var result = await DnsService.SetDnsAsync(name, servers);
            DnsActionStatusText.Text = result.Message;
            DnsActionStatusText.Foreground = result.Success ? BrushFrom("#77E4BF") : BrushFrom("#FF9B9B");
            if (result.Success) await Task.Delay(600);
            RefreshDnsStatus();
        }
        catch (Exception ex) { DnsActionStatusText.Text = ex.Message; DnsActionStatusText.Foreground = BrushFrom("#FF9B9B"); }
    }

    private async void ResetDns_Click(object sender, RoutedEventArgs e)
    {
        var name = GetSelectedInterfaceName();
        if (string.IsNullOrWhiteSpace(name)) { DnsActionStatusText.Text = "اول یک کارت شبکه‌ی فعال انتخاب کن."; return; }
        DnsActionStatusText.Text = "درخواست بازگردانی DNS به حالت خودکار…";
        var result = await DnsService.ResetDnsAsync(name);
        DnsActionStatusText.Text = result.Message;
        DnsActionStatusText.Foreground = result.Success ? BrushFrom("#77E4BF") : BrushFrom("#FF9B9B");
        RefreshDnsStatus();
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        DnsActionStatusText.Text = "درخواست پاک‌کردن DNS Cache…";
        var result = await DnsService.FlushDnsAsync();
        DnsActionStatusText.Text = result.Message;
        DnsActionStatusText.Foreground = result.Success ? BrushFrom("#77E4BF") : BrushFrom("#FF9B9B");
        PingResultText.Text = result.Success ? "DNS Cache ویندوز پاک شد." : result.Message;
    }

    private async void RunPing_Click(object sender, RoutedEventArgs e)
    {
        PingResultText.Text = "در حال تست…";
        var result = await DnsService.PingAsync(PingTargetBox.Text);
        PingResultText.Text = result;
    }

    private void RefreshDnsStatus_Click(object sender, RoutedEventArgs e) { PopulateNetworkInterfaces(); RefreshDnsStatus(); }

    private void ShowWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_widget?.IsVisible == true) { _widget.Hide(); LocalSettings.Current.ShowWidget = false; }
        else { LocalSettings.Current.ShowWidget = true; EnsureWidgetVisible(); }
        ShowWidgetCheck.IsChecked = LocalSettings.Current.ShowWidget;
        LocalSettings.SaveSettings();
    }

    private void EnsureWidgetVisible()
    {
        if (_widget is null) _widget = new FloatingWidget();
        if (!_widget.IsVisible) _widget.Show();
        _widget.Topmost = true;
    }

    private void RefreshConnectionBadge()
    {
        var configured = _api?.IsConfigured == true;
        BackendStatusText.Text = configured ? "Supabase تنظیم شده" : "Supabase تنظیم نشده";
        ConnectionDot.Fill = BrushFrom(configured ? "#52E3B6" : "#E3B45E");
        SettingsApiStatusText.Text = configured ? "Project URL و کلید عمومی تنظیم شده‌اند؛ اتصال واقعی با درخواست HTTPS بررسی می‌شود." : "Project URL و Publishable/anon key سوپربیس را وارد کن.";
    }

    private void SaveApiSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _api.SetBaseUrl(ApiUrlBox.Text);
            _api.SetPublishableKey(SupabasePublishableKeyBox.Text);
            LocalSettings.Current.ApiBaseUrl = _api.BaseUrl;
            LocalSettings.Current.SupabasePublishableKey = _api.PublishableKey;
            LocalSettings.SaveSettings();
            RefreshConnectionBadge();
            SettingsApiStatusText.Text = _api.IsConfigured ? "تنظیمات ذخیره شد. در حال بررسی دسترسی Supabase…" : "اطلاعات اتصال پاک یا ناقص است؛ قابلیت‌های آنلاین خاموش هستند.";
            if (_api.IsConfigured) _ = TestApiConnectionAsync();
        }
        catch (Exception ex) { SettingsApiStatusText.Text = ex.Message; }
    }

    private async Task TestApiConnectionAsync()
    {
        try
        {
            var lobbies = await _api.GetLobbiesAsync();
            SettingsApiStatusText.Text = $"اتصال موفق است. Supabase پاسخ داد؛ {lobbies.Count} لابی باز موجود است.";
            ConnectionDot.Fill = BrushFrom("#52E3B6");
        }
        catch (Exception ex) { SettingsApiStatusText.Text = "API قابل‌دسترسی نیست: " + ex.Message; ConnectionDot.Fill = BrushFrom("#FF9B9B"); }
    }

    private void SaveAppPreferences_Click(object sender, RoutedEventArgs e)
    {
        LocalSettings.Current.ShowWidget = ShowWidgetCheck.IsChecked == true;
        LocalSettings.Current.StartWithWindows = StartupCheck.IsChecked == true;
        try
        {
            SetAutoStart(LocalSettings.Current.StartWithWindows);
            if (LocalSettings.Current.ShowWidget) EnsureWidgetVisible(); else _widget?.Hide();
            LocalSettings.SaveSettings();
            SettingsApiStatusText.Text = "تنظیمات محلی ذخیره شدند.";
        }
        catch (Exception ex) { SettingsApiStatusText.Text = "ذخیره‌ی تنظیمات شروع خودکار ناموفق بود: " + ex.Message; }
    }

    private static void SetAutoStart(bool enabled)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(runKey, writable: true)
            ?? throw new InvalidOperationException("کلید رجیستری شروع خودکار قابل ایجاد نیست.");
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) throw new InvalidOperationException("مسیر فایل اجرایی تشخیص داده نشد.");
            key.SetValue("MIXO6", $"\"{exe}\"");
        }
        else key.DeleteValue("MIXO6", throwOnMissingValue: false);
    }

    private static int AvatarIndex(string avatar) => avatar switch { "✦" => 1, "⬡" => 2, "◆" => 3, "◉" => 4, _ => 0 };
    private string GetSelectedAvatar()
    {
        if (ProfileAvatarCombo.SelectedItem is ComboBoxItem item && item.Tag is string avatar) return avatar;
        return LocalSettings.Current.Avatar;
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RegisterUsernameBox.Text.Trim().Length < 3) throw new InvalidOperationException("Username باید دست‌کم ۳ کاراکتر داشته باشد.");
            VerifyEmailBox.Text = RegisterEmailBox.Text.Trim();
            await _api.RegisterAsync(RegisterUsernameBox.Text.Trim(), RegisterEmailBox.Text.Trim(), RegisterPasswordBox.Password);
            AuthStateText.Text = "درخواست ثبت‌نام ارسال شد؛ اگر ایمیل جدید باشد، کد تأیید دریافت می‌کنی.";
            AuthStateText.Foreground = BrushFrom("#77E4BF");
            MessageBox.Show("اگر این ایمیل حساب جدیدی باشد، پیام تأیید ارسال می‌شود. Inbox و Spam را بررسی کن و کد را در بخش تأیید وارد کن. اگر ایمیلی نرسید، تنظیم SMTP پروژه را بررسی کن.", "MIXO6 · ثبت‌نام", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { UpdateAuthState(ex.Message, false); }
    }

    private async void VerifyEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _api.VerifyEmailAsync(VerifyEmailBox.Text.Trim(), VerifyCodeBox.Text.Trim());
            await AfterSignInAsync();
        }
        catch (Exception ex) { UpdateAuthState(ex.Message, false); }
    }

    private async void ResendVerification_Click(object sender, RoutedEventArgs e)
    {
        try { await _api.ResendVerificationAsync(VerifyEmailBox.Text.Trim()); UpdateAuthState("اگر این ایمیل در انتظار تأیید باشد، کد جدید ارسال شده است.", false); }
        catch (Exception ex) { UpdateAuthState(ex.Message, false); }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try { await _api.LoginAsync(LoginIdentifierBox.Text.Trim(), LoginPasswordBox.Password); await AfterSignInAsync(); }
        catch (Exception ex) { UpdateAuthState(ex.Message, false); }
    }

    private async Task AfterSignInAsync()
    {
        try
        {
            var me = await _api.GetMeAsync();
            ApplyRemoteUser(me);
        }
        catch (Exception ex)
        {
            UpdateAuthState("توکن ورود دریافت شد، اما بارگذاری پروفایل ناموفق بود: " + ex.Message, false);
        }
        RefreshConnectionBadge();
        await RefreshLobbiesCoreAsync(showStatus: false);
    }

    private void ApplyRemoteUser(ApiUser user)
    {
        LocalSettings.Current.DisplayName = user.Username;
        LocalSettings.Current.Avatar = user.Avatar;
        LocalSettings.Current.MainGame = user.MainGame;
        LocalSettings.SaveSettings();
        SidebarUsernameText.Text = user.Username;
        ProfileMainGameBox.Text = user.MainGame;
        ProfileAvatarCombo.SelectedIndex = AvatarIndex(user.Avatar);
        UpdateAuthState("وارد شده‌ای · " + user.Username, true);
    }

    private void ReflectAccountState()
    {
        SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        if (_api.IsSignedIn) UpdateAuthState("نشست ذخیره شده · " + _api.Session!.Username, true);
        else UpdateAuthState("وارد نشده‌ای", false);
    }

    private void UpdateAuthState(string message, bool success)
    {
        AuthStateText.Text = message;
        AuthStateText.Foreground = BrushFrom(success ? "#77E4BF" : "#E7BD72");
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _api.SignOutAsync();
        SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        UpdateAuthState("از حساب خارج شدی.", false);
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var avatar = GetSelectedAvatar();
        var game = ProfileMainGameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(game) || game.Length > 60) { ProfileSaveStatusText.Text = "نام بازی را درست وارد کن (حداکثر ۶۰ کاراکتر)."; return; }
        try
        {
            LocalSettings.Current.Avatar = avatar;
            LocalSettings.Current.MainGame = game;
            LocalSettings.SaveSettings();
            if (_api.IsSignedIn) await _api.UpdateProfileAsync(avatar, game);
            ProfileSaveStatusText.Text = _api.IsSignedIn ? "پروفایل محلی و آنلاین ذخیره شد." : "پروفایل محلی ذخیره شد؛ برای همگام‌سازی وارد حساب شو.";
            SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        }
        catch (Exception ex) { ProfileSaveStatusText.Text = "پروفایل محلی ذخیره شد؛ آنلاین ناموفق بود: " + ex.Message; }
    }

    private async void CreateLobby_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_api.IsSignedIn) throw new InvalidOperationException("برای ساخت لابی باید وارد حساب تأییدشده شوی.");
            var req = new CreateLobbyRequest { Game = LobbyGameBox.Text.Trim(), ServerAddress = LobbyServerAddressBox.Text.Trim(), ServerLabel = LobbyServerLabelBox.Text.Trim(), Description = LobbyDescriptionBox.Text.Trim() };
            LobbyStatusText.Text = "در حال ساخت لابی…";
            await _api.CreateLobbyAsync(req);
            LobbyStatusText.Text = "لابی ساخته شد و در فهرست قرار گرفت.";
            await RefreshLobbiesCoreAsync(showStatus: false);
        }
        catch (Exception ex) { LobbyStatusText.Text = ex.Message; }
    }

    private async void RefreshLobbies_Click(object sender, RoutedEventArgs e) => await RefreshLobbiesCoreAsync(showStatus: true);

    private async Task RefreshLobbiesCoreAsync(bool showStatus)
    {
        if (!_api.IsConfigured)
        {
            LobbiesList.Children.Clear();
            LobbiesList.Children.Add(new TextBlock { Text = "Supabase هنوز تنظیم نشده است. از Settings، Project URL و کلید عمومی را وارد کن.", Foreground = BrushFrom("#A9A6BB"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 14), FlowDirection = FlowDirection.RightToLeft });
            if (showStatus) LobbyStatusText.Text = "اتصال Supabase تنظیم نشده است.";
            return;
        }
        try
        {
            if (showStatus) LobbyStatusText.Text = "در حال دریافت لابی‌های باز از Supabase…";
            var lobbies = await _api.GetLobbiesAsync();
            LobbiesList.Children.Clear();
            if (lobbies.Count == 0)
            {
                LobbiesList.Children.Add(new TextBlock { Text = "فعلاً لابی بازی‌ای وجود ندارد. اولین لابی را بساز!", Foreground = BrushFrom("#A9A6BB"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 14), FlowDirection = FlowDirection.RightToLeft });
            }
            foreach (var lobby in lobbies) LobbiesList.Children.Add(BuildLobbyCard(lobby));
            if (showStatus) LobbyStatusText.Text = $"{lobbies.Count} لابی باز پیدا شد.";
        }
        catch (Exception ex)
        {
            if (showStatus) LobbyStatusText.Text = "دریافت لابی‌ها ناموفق بود: " + ex.Message;
            if (LobbiesList.Children.Count == 0) LobbiesList.Children.Add(new TextBlock { Text = "به Supabase وصل نشدیم. Project URL، کلید عمومی و دسترسی اینترنت را بررسی کن.", Foreground = BrushFrom("#FF9B9B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 14), FlowDirection = FlowDirection.RightToLeft });
        }
    }

    private Border BuildLobbyCard(LobbyDto lobby)
    {
        var border = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(16) };
        var root = new Grid { FlowDirection = FlowDirection.RightToLeft };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = lobby.Game, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = BrushFrom("#F7F4FF"), Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(new Border { Background = BrushFrom("#2B213A"), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 3), Child = new TextBlock { Text = lobby.Status == "open" ? "OPEN" : lobby.Status.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = BrushFrom("#F0A2DC") } });
        info.Children.Add(top);
        info.Children.Add(new TextBlock { Text = $"سازنده: {lobby.CreatorUsername}   ·   {lobby.CreatorAvatar}   ·   {lobby.ServerLabel}   ·   {lobby.MemberCount}/2 نفر", FontSize = 11, Foreground = BrushFrom("#A7A3B8"), Margin = new Thickness(0, 7, 0, 0) });
        if (!string.IsNullOrWhiteSpace(lobby.Description)) info.Children.Add(new TextBlock { Text = lobby.Description, FontSize = 12, Foreground = BrushFrom("#D0CDDB"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 620 });
        var profileButton = new Button { Content = "نمایش پروفایل سازنده", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(9, 6, 9, 6), HorizontalAlignment = HorizontalAlignment.Left, Tag = lobby.CreatorUsername };
        profileButton.Click += ViewCreator_Click;
        info.Children.Add(profileButton);
        Grid.SetColumn(info, 0); root.Children.Add(info);
        bool ownLobby = _api.IsSignedIn && string.Equals(_api.Session?.Username, lobby.CreatorUsername, StringComparison.OrdinalIgnoreCase);
        var join = new Button { Content = ownLobby ? "بستن لابی" : "جوین دادن  ↗", Style = (Style)FindResource(ownLobby ? "GhostButton" : "AccentButton"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = lobby.Id, MinWidth = 112 };
        join.Click += ownLobby ? CloseLobby_Click : JoinLobby_Click;
        Grid.SetColumn(join, 1); root.Children.Add(join);
        border.Child = root;
        return border;
    }


    private async void CloseLobby_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not long id) return;
        try
        {
            await _api.CloseLobbyAsync(id);
            LobbyStatusText.Text = "لابی بسته شد و دیگر در فهرست عمومی نمایش داده نمی‌شود.";
            await RefreshLobbiesCoreAsync(showStatus: false);
        }
        catch (Exception ex) { LobbyStatusText.Text = ex.Message; }
    }

    private async void JoinLobby_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not long id) return;
        try
        {
            if (!_api.IsSignedIn) throw new InvalidOperationException("برای جوین‌دادن باید وارد حساب تأییدشده شوی.");
            var result = await _api.JoinLobbyAsync(id);
            _joinedServerAddress = result.ServerAddress;
            JoinedLobbyAddressText.Text = result.ServerAddress;
            JoinedLobbyResultBorder.Visibility = Visibility.Visible;
            LobbyStatusText.Text = $"به لابی {result.Game} از {result.CreatorUsername} پیوستی. آدرس سرور در کارت بالا نمایش داده شد.";
            try { Clipboard.SetText(result.ServerAddress); }
            catch { LobbyStatusText.Text += " (کپی خودکار انجام نشد؛ آدرس را دستی کپی کن.)"; }
            await RefreshLobbiesCoreAsync(showStatus: false);
        }
        catch (Exception ex) { LobbyStatusText.Text = ex.Message; }
    }

    private void CopyJoinedAddress_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_joinedServerAddress)) return;
        try { Clipboard.SetText(_joinedServerAddress); LobbyStatusText.Text = "آدرس سرور کپی شد."; }
        catch (Exception ex) { LobbyStatusText.Text = "کپی ناموفق بود: " + ex.Message; }
    }

    private async void ViewCreator_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string username) return;
        try
        {
            var profile = await _api.GetPublicProfileAsync(username);
            var status = profile.Online ? "آنلاین" : profile.LastSeenAt is null ? "وضعیت نامشخص" : "آخرین فعالیت: " + profile.LastSeenAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
            MessageBox.Show($"Username: {profile.Username}\nوضعیت: {status}\nبازی اصلی: {profile.MainGame}\nآواتار: {profile.Avatar}", "پروفایل بازیکن · MIXO6", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { LobbyStatusText.Text = "دریافت پروفایل ناموفق بود: " + ex.Message; }
    }

    private void OpenFarsiOrigin_Click(object sender, RoutedEventArgs e) => OpenUrl("https://origin-core.github.io/Farsi-Origin/");
    private void OpenRtxGame_Click(object sender, RoutedEventArgs e) => OpenUrl("https://rtx-game.rtx-game.workers.dev/");
    private void OpenVidox_Click(object sender, RoutedEventArgs e) => OpenUrl("https://vidox-ir.github.io/toolbox/");
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("بازکردن لینک ناموفق بود: " + ex.Message, "MIXO6", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new BrushConverter().ConvertFromString(color) as SolidColorBrush ?? new SolidColorBrush(Colors.White);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
