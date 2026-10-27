using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MIXO6.Services;

/// <summary>
/// Reads real frame-presentation timings from the separately shipped PresentMon console utility.
/// It deliberately reports no number until the CSV contains valid frame samples.
/// </summary>
public sealed class FpsMonitorService : IDisposable
{
    private const int TailBytes = 512 * 1024;
    private readonly string _toolPath = Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
    private readonly string _csvPath = Path.Combine(LocalSettings.RootPath, "fps_capture.csv");
    private Process? _process;

    public string TargetProcessName { get; private set; } = "";
    public string? LastError { get; private set; }
    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    public void Start(string processName, bool runElevated)
    {
        var exeName = Path.GetFileName((processName ?? "").Trim());
        if (!Regex.IsMatch(exeName, @"^[A-Za-z0-9_. -]{1,120}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("فایل اجرایی بازی معتبر نیست؛ فهرست بازی‌ها را تازه‌سازی کن.", nameof(processName));
        if (!File.Exists(_toolPath))
            throw new FileNotFoundException("ابزار اندازه‌گیری FPS همراه برنامه یافت نشد. یک بار نسخه‌ی جدید MIXO6 را از GitHub Actions دریافت کن.", _toolPath);

        Stop();
        LastError = null;
        TargetProcessName = exeName;
        Directory.CreateDirectory(LocalSettings.RootPath);
        try { if (File.Exists(_csvPath)) File.Delete(_csvPath); } catch (IOException) { }

        var startInfo = new ProcessStartInfo
        {
            FileName = _toolPath,
            Arguments = $"--process_name \"{exeName}\" --output_file \"{_csvPath}\" --timed 3600 --terminate_after_timed --v1_metrics",
            UseShellExecute = runElevated,
            CreateNoWindow = !runElevated,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(_toolPath) ?? AppContext.BaseDirectory
        };
        if (runElevated) startInfo.Verb = "runas";

        try
        {
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("ابزار PresentMon اجرا نشد.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("درخواست دسترسی مدیر لغو شد؛ پایش FPS شروع نشد.", ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("اجرای ابزار اندازه‌گیری FPS ناموفق بود. Windows Security یا دسترسی ابزار را بررسی کن.", ex);
        }
    }

    public double? ReadFps()
    {
        try
        {
            if (!File.Exists(_csvPath)) return null;
            string? header;
            using (var headerStream = new FileStream(_csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var headerReader = new StreamReader(headerStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false))
                header = headerReader.ReadLine();
            if (string.IsNullOrWhiteSpace(header)) return null;

            var names = SplitCsv(header);
            var frameIndex = IndexOf(names, "MsBetweenPresents");
            if (frameIndex < 0) frameIndex = IndexOf(names, "MsBetweenDisplayChange");
            if (frameIndex < 0) frameIndex = IndexOf(names, "FrameTimeMs");
            if (frameIndex < 0) return null;

            var values = new Queue<double>();
            using var stream = new FileStream(_csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Position = start;
            if (start > 0)
            {
                // Discard a partial row at the start of the tail window.
                int b;
                while ((b = stream.ReadByte()) != -1 && b != '\n') { }
            }
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = SplitCsv(line);
                if (frameIndex >= cells.Count) continue;
                if (!double.TryParse(cells[frameIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) continue;
                if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 1 || ms > 2000) continue;
                values.Enqueue(ms);
                while (values.Count > 90) values.Dequeue();
            }
            if (values.Count < 3) return null;
            var averageMs = values.Average();
            if (averageMs <= 0) return null;
            return Math.Round(Math.Clamp(1000d / averageMs, 1d, 1000d), 1);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.NotSupportedException)
        {
            LastError = "توقف ابزار کامل نشد؛ پایش ابزار حداکثر پس از یک ساعت خودکار خاتمه می‌یابد.";
        }
        finally { process.Dispose(); }
    }

    private static int IndexOf(IReadOnlyList<string> items, string wanted)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i].Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                result.Add(cell.ToString()); cell.Clear();
            }
            else cell.Append(ch);
        }
        result.Add(cell.ToString());
        return result;
    }

    public void Dispose() => Stop();
}
