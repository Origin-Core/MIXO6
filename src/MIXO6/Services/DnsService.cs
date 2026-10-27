using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace MIXO6.Services;

public sealed record DnsApplyResult(bool Success, string Message);

public static class DnsService
{
    private const string LogPathToken = "__MIXO6_RESULT_LOG__";
    public static IReadOnlyList<NetworkInterface> GetInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel && n.OperationalStatus == OperationalStatus.Up)
        .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        .ThenBy(n => n.Name).ToList();

    public static IReadOnlyList<string> GetDns(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties().DnsAddresses.Select(x => x.ToString()).Distinct().ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public static Task<DnsApplyResult> SetDnsAsync(string interfaceName, params string[] servers) => Task.Run(() => RunElevatedAsync(BuildSetDnsScript(interfaceName, servers)));

    public static Task<DnsApplyResult> ResetDnsAsync(string interfaceName) => Task.Run(() => RunElevatedAsync($"$ErrorActionPreference='Stop'\ntrap {{ $_ | Out-String | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8; exit 1 }}\nSet-DnsClientServerAddress -InterfaceAlias {PsQuote(interfaceName)} -ResetServerAddresses\n'OK' | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8"));

    public static Task<DnsApplyResult> FlushDnsAsync() => Task.Run(() => RunElevatedAsync($"$ErrorActionPreference='Stop'\ntrap {{ $_ | Out-String | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8; exit 1 }}\nClear-DnsClientCache\n'OK' | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8"));

    public static async Task<string> PingAsync(string target)
    {
        target = target.Trim();
        if (string.IsNullOrWhiteSpace(target)) return "یک IP یا دامنه وارد کن.";
        if (target.Length > 253 || target.Any(char.IsWhiteSpace)) return "آدرس واردشده معتبر نیست.";
        try
        {
            using var ping = new Ping();
            var times = new List<long>();
            var lastStatus = IPStatus.Unknown;
            int total = 4;
            for (int i = 0; i < total; i++)
            {
                var reply = await ping.SendPingAsync(target, 1200).ConfigureAwait(false);
                lastStatus = reply.Status;
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                if (i < total - 1) await Task.Delay(180).ConfigureAwait(false);
            }
            int lost = total - times.Count;
            if (times.Count == 0) return $"هیچ پاسخی دریافت نشد · {lastStatus} · اتلاف بسته ۱۰۰٪";
            return $"پاسخ {times.Count}/{total} · میانگین {times.Average():0.0} ms · کمینه {times.Min()} · بیشینه {times.Max()} · اتلاف {lost * 100 / total}%";
        }
        catch (Exception ex) { return "تست ناموفق بود: " + ex.Message; }
    }

    private static string BuildSetDnsScript(string interfaceName, string[] servers)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) || interfaceName.Length > 200) throw new ArgumentException("کارت شبکه معتبر نیست.");
        if (servers.Length is < 1 or > 4) throw new ArgumentException("یک تا چهار DNS وارد کنید.");
        var parsed = new List<IPAddress>(servers.Length);
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out var address)) throw new ArgumentException($"DNS معتبر نیست: {server}");
            parsed.Add(address);
        }
        if (parsed.Select(x => x.AddressFamily).Distinct().Count() != 1)
            throw new ArgumentException("DNS اصلی و جایگزین باید هر دو IPv4 یا هر دو IPv6 باشند.");
        var family = parsed[0].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6";
        var list = string.Join(",", parsed.Select(x => PsQuote(x.ToString())));
        return $"$ErrorActionPreference='Stop'\ntrap {{ $_ | Out-String | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8; exit 1 }}\n$expected = @({list})\nSet-DnsClientServerAddress -InterfaceAlias {PsQuote(interfaceName)} -ServerAddresses $expected\n$actual = @(Get-DnsClientServerAddress -InterfaceAlias {PsQuote(interfaceName)} -AddressFamily {family} | Select-Object -ExpandProperty ServerAddresses)\nif (@(Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -gt 0) {{ throw 'Windows DNS verification failed.' }}\n("OK: " + ($actual -join ', ')) | Set-Content -LiteralPath {PsQuote(LogPathToken)} -Encoding UTF8";
    }

    private static DnsApplyResult RunElevatedAsync(string script)
    {
        string operationId = Guid.NewGuid().ToString("N");
        string scriptPath = Path.Combine(Path.GetTempPath(), "mixo6-dns-" + operationId + ".ps1");
        string logPath = Path.Combine(Path.GetTempPath(), "mixo6-dns-" + operationId + ".result.txt");
        script = script.Replace(PsQuote(LogPathToken), PsQuote(logPath), StringComparison.Ordinal);
        try
        {
            if (File.Exists(logPath)) File.Delete(logPath);
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var proc = Process.Start(psi);
            if (proc is null) return new DnsApplyResult(false, "اجرای ابزار مدیریتی شروع نشد.");
            proc.WaitForExit();
            var output = File.Exists(logPath) ? File.ReadAllText(logPath).Trim().Trim('\uFEFF') : "";
            if (proc.ExitCode == 0 && output.StartsWith("OK", StringComparison.OrdinalIgnoreCase)) return new DnsApplyResult(true, "عملیات با موفقیت روی ویندوز اجرا شد.");
            return new DnsApplyResult(false, string.IsNullOrWhiteSpace(output) ? "عملیات ناموفق بود. کارت شبکه و مجوز Administrator را بررسی کن." : output);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new DnsApplyResult(false, "درخواست دسترسی Administrator لغو شد؛ هیچ تغییری اعمال نشد.");
        }
        catch (Exception ex) { return new DnsApplyResult(false, "خطا در اجرای تنظیمات شبکه: " + ex.Message); }
        finally { try { File.Delete(scriptPath); } catch { } try { File.Delete(logPath); } catch { } }
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
