using System.IO;
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
    private readonly FpsMonitorService _fps = new();
    private readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _connectionChecked;
    private bool _connectionHealthy;
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
        ("تنظیمات", "شخصی‌سازی رفتار و بررسی سرویس‌های آنلاین")
    };

    public MainWindow()
    {
        LocalSettings.Load();
        InitializeComponent();
        _api = new ApiClient(LocalSettings.Current.ApiBaseUrl, LocalSettings.Current.SupabasePublishableKey);
        ProfileMainGameBox.Text = LocalSettings.Current.MainGame;
        ProfileAvatarCombo.SelectedIndex = AvatarIndex(LocalSettings.Current.Avatar);
        ShowWidgetCheck.IsChecked = LocalSettings.Current.ShowWidget;
        StartupCheck.IsChecked = LocalSettings.Current.StartWithWindows;
        SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        NetworkInterfaceCombo.SelectionChanged += (_, _) => RefreshDnsStatus();
        _sensorTimer.Tick += async (_, _) => await RefreshTemperaturesAsync();
        _fpsTimer.Tick += (_, _) => RefreshFpsReading();
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
        RefreshFpsTargets();
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
        if (_api.IsConfigured)
        {
            await TestApiConnectionAsync();
            await RefreshLobbiesCoreAsync(showStatus: false);
        }
        else RefreshConnectionBadge();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _sensorTimer.Stop();
        _presenceTimer.Stop();
        _fpsTimer.Stop();
        _fps.Dispose();
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
        if (index == 6) RefreshConnectionBadge();
        var panel = panels[index];
        panel.Opacity = 0;
        var motion = new TranslateTransform(0, 13);
        panel.RenderTransform = motion;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(13, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        panel.BeginAnimation(UIElement.OpacityProperty, fade);
        motion.BeginAnimation(TranslateTransform.YProperty, slide);
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
            TopCpuTempText.Text = snapshot.CpuCelsius is double topCpu ? $"{topCpu:0}°C" : "--°C";
            TopGpuTempText.Text = snapshot.GpuCelsius is double topGpu ? $"{topGpu:0}°C" : "--°C";
            CpuTempBar.Value = snapshot.CpuCelsius is double cv ? Math.Clamp(cv, 0, 110) : 0;
            GpuTempBar.Value = snapshot.GpuCelsius is double gv ? Math.Clamp(gv, 0, 110) : 0;
            CpuSensorNameText.Text = snapshot.CpuCelsius is null ? "سنسور CPU در دسترس نیست" : Shorten(snapshot.CpuName, 54);
            GpuSensorNameText.Text = snapshot.GpuCelsius is null ? "سنسور GPU در دسترس نیست" : Shorten(snapshot.GpuName, 54);
            CpuTempCaption.Text = TempCaption(snapshot.CpuCelsius);
            GpuTempCaption.Text = TempCaption(snapshot.GpuCelsius);
            SensorStatusText.Text = snapshot.Status;
            ElevateSensorButton.Visibility = snapshot.CpuCelsius is null || snapshot.GpuCelsius is null ? Visibility.Visible : Visibility.Collapsed;
            _widget?.UpdateTemps(snapshot.CpuCelsius, snapshot.GpuCelsius);
            _widget?.UpdateFps(_fps.ReadFps());
        }
        catch (Exception ex) { SensorStatusText.Text = "خواندن سنسورها ناموفق بود: " + ex.Message; }
        finally { Volatile.Write(ref _sensorBusy, 0); }
    }

    private async void RefreshSensors_Click(object sender, RoutedEventArgs e) => await RefreshTemperaturesAsync();

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        var executable = Environment.ProcessPath;
        if (executable is null || string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            SensorStatusText.Text = "مسیر اجرای برنامه مشخص نیست؛ اجرای مجدد با مجوز مدیر ممکن نشد.";
            return;
        }
        if (MessageBox.Show("برخی سنسورهای سخت‌افزاری در ویندوز به مجوز مدیر نیاز دارند. با ادامه، MIXO6 دوباره اجرا می‌شود و ویندوز پنجره‌ی UAC را نشان می‌دهد. فقط اگر خودت تأیید کنی اجرا خواهد شد.", "دسترسی سنسورهای سخت‌افزار", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SensorStatusText.Text = "درخواست UAC لغو شد؛ برنامه با سطح دسترسی فعلی ادامه می‌دهد.";
        }
        catch (Exception ex) { SensorStatusText.Text = "اجرای مجدد ناموفق بود: " + ex.Message; }
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

    private void RefreshFpsTargets_Click(object sender, RoutedEventArgs e) => RefreshFpsTargets();

    private void RefreshFpsTargets()
    {
        var previous = (FpsTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                var name = process.ProcessName + ".exe";
                if (!targets.ContainsKey(name)) targets[name] = process.MainWindowTitle;
            }
            catch { /* Some protected/system processes do not expose window details. */ }
            finally { process.Dispose(); }
        }
        FpsTargetCombo.Items.Clear();
        foreach (var item in targets.OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase))
            FpsTargetCombo.Items.Add(new ComboBoxItem { Content = item.Value + "  ·  " + item.Key, Tag = item.Key });
        var preferred = FpsTargetCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), previous, StringComparison.OrdinalIgnoreCase));
        FpsTargetCombo.SelectedItem = preferred ?? FpsTargetCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        if (FpsTargetCombo.Items.Count == 0)
            FpsStatusText.Text = "بازیِ دارای پنجره پیدا نشد؛ بازی را اجرا کن و دوباره فهرست را بازخوانی کن.";
        else
            FpsStatusText.Text = $"{FpsTargetCombo.Items.Count} برنامه‌ی دارای پنجره پیدا شد؛ بازی موردنظر را انتخاب کن.";
    }

    private void StartFpsMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (FpsTargetCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string processName)
        {
            FpsStatusText.Text = "ابتدا بازی را باز کن و از فهرست انتخابش کن.";
            return;
        }
        var elevated = FpsRunElevatedCheck.IsChecked == true;
        if (elevated && MessageBox.Show(
                "برای اندازه‌گیری FPS از PresentMon استفاده می‌شود. با تأیید ادامه، پنجره‌ی رسمی UAC ویندوز برای ابزار اندازه‌گیری باز می‌شود. این دسترسی فقط با انتخاب خودت درخواست خواهد شد.",
                "اجازه‌ی اندازه‌گیری فریم", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
        {
            FpsStatusText.Text = "درخواست دسترسی لغو شد؛ پایش شروع نشد.";
            return;
        }
        try
        {
            _fps.Start(processName, elevated);
            _fpsTimer.Start();
            FpsStatusText.Text = $"در حال دریافت فریم واقعی از {processName}؛ ممکن است چند ثانیه طول بکشد.";
            RefreshFpsReading();
        }
        catch (Exception ex)
        {
            FpsStatusText.Text = ex.Message;
            FpsText.Text = "--";
            TopFpsText.Text = "--";
            _widget?.UpdateFps(null);
        }
    }

    private void StopFpsMonitor_Click(object sender, RoutedEventArgs e)
    {
        _fpsTimer.Stop();
        _fps.Stop();
        FpsText.Text = "--";
        TopFpsText.Text = "--";
        _widget?.UpdateFps(null);
        FpsStatusText.Text = string.IsNullOrWhiteSpace(_fps.LastError) ? "پایش FPS متوقف شد." : _fps.LastError;
    }

    private void RefreshFpsReading()
    {
        var fps = _fps.ReadFps();
        var text = fps is double value ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "--";
        FpsText.Text = text;
        TopFpsText.Text = text;
        _widget?.UpdateFps(fps);
        if (!_fps.IsRunning)
        {
            _fpsTimer.Stop();
            if (!string.IsNullOrWhiteSpace(_fps.LastError)) FpsStatusText.Text = _fps.LastError;
            else if (string.IsNullOrWhiteSpace(_fps.TargetProcessName)) FpsStatusText.Text = "پایش FPS شروع نشده است.";
            else FpsStatusText.Text = "ضبط فریم متوقف شده است؛ برای شروع دوباره، بازی را انتخاب کن.";
        }
        else if (fps is double actual)
            FpsStatusText.Text = $"{_fps.TargetProcessName}  ·  نرخ میانگین فریم‌های اخیر: {actual:0.0} FPS";
        else
            FpsStatusText.Text = "در انتظار نمونه‌ی واقعی فریم… بازی باید باز باشد؛ اگر داده نیامد، گزینه‌ی UAC را فعال و پایش را دوباره شروع کن.";
    }

    private void RefreshConnectionBadge()
    {
        if (!_connectionChecked)
        {
            BackendStatusText.Text = "بررسی سرویس";
            ConnectionDot.Fill = BrushFrom("#E3B45E");
            if (SettingsApiStatusText is not null) SettingsApiStatusText.Text = _api.IsConfigured
                ? "برای بررسی اتصال واقعی، روی «بررسی اتصال» بزن. هیچ کلید یا نشانی در رابط نمایش داده نمی‌شود."
                : "تنظیمات داخلی سرویس آنلاین در دسترس نیست؛ نسخه‌ی نصب‌شده را از انتشار رسمی دریافت کن.";
            return;
        }
        BackendStatusText.Text = _connectionHealthy ? "آنلاین" : "آفلاین";
        ConnectionDot.Fill = BrushFrom(_connectionHealthy ? "#52E3B6" : "#FF9B9B");
    }

    private async void CheckServices_Click(object sender, RoutedEventArgs e) => await TestApiConnectionAsync();

    private async Task TestApiConnectionAsync()
    {
        SettingsApiStatusText.Text = "در حال بررسی اتصال HTTPS و دسترسی سرویس‌های آنلاین…";
        try
        {
            var lobbies = await _api.GetLobbiesAsync();
            _connectionChecked = true;
            _connectionHealthy = true;
            SettingsApiStatusText.Text = $"اتصال برقرار است؛ {lobbies.Count} لابی عمومی در دسترس است. قابلیت‌های حساب و لابی اکنون می‌توانند با سرویس ارتباط بگیرند.";
        }
        catch (Exception ex)
        {
            _connectionChecked = true;
            _connectionHealthy = false;
            SettingsApiStatusText.Text = "اتصال به سرویس آنلاین برقرار نشد: " + ex.Message;
        }
        RefreshConnectionBadge();
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
            SettingsPreferencesStatusText.Text = "تنظیمات محلی ذخیره شدند.";
        }
        catch (Exception ex) { SettingsPreferencesStatusText.Text = "ذخیره‌ی تنظیمات شروع خودکار ناموفق بود: " + ex.Message; }
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
            VerifyEmailBox.Text = RegisterEmailBox.Text.Trim();
            AuthTabs.SelectedIndex = 1;
            UpdateAuthState("درخواست ثبت‌نام پذیرفته شد. Inbox و Spam را بررسی کن؛ ارسال واقعی ایمیل به تنظیمات سرویس ایمیل وابسته است.", true);
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
        try { await _api.ResendVerificationAsync(VerifyEmailBox.Text.Trim()); UpdateAuthState("درخواست ارسال مجدد پذیرفته شد؛ Inbox و Spam را بررسی کن. این پیام به‌تنهایی تضمین تحویل ایمیل نیست.", true); }
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
        AuthTabs.SelectedIndex = 3;
        UpdateAuthState("وارد شده‌ای · " + user.Username, true);
    }

    private void ReflectAccountState()
    {
        SidebarUsernameText.Text = LocalSettings.Current.DisplayName;
        if (_api.IsSignedIn)
        {
            AuthTabs.SelectedIndex = 3;
            UpdateAuthState("نشست ذخیره شده · " + _api.Session!.Username, true);
        }
        else
        {
            if (AuthTabs.SelectedIndex == 3) AuthTabs.SelectedIndex = 0;
            UpdateAuthState("وارد نشده‌ای", false);
        }
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
            LobbiesList.Children.Add(new TextBlock { Text = "سرویس آنلاین در دسترس نیست. اتصال شبکه و نسخه‌ی نصب‌شده را بررسی کن.", Foreground = BrushFrom("#A9A6BB"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 14), FlowDirection = FlowDirection.RightToLeft });
            if (showStatus) LobbyStatusText.Text = "سرویس آنلاین پیکربندی نشده است.";
            return;
        }
        try
        {
            if (showStatus) LobbyStatusText.Text = "در حال دریافت لابی‌های عمومی…";
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
            if (LobbiesList.Children.Count == 0) LobbiesList.Children.Add(new TextBlock { Text = "به سرویس آنلاین وصل نشدیم. اتصال اینترنت یا وضعیت سرور را بررسی کن.", Foreground = BrushFrom("#FF9B9B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 14), FlowDirection = FlowDirection.RightToLeft });
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
