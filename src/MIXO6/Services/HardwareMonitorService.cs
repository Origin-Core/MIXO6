using LibreHardwareMonitor.Hardware;

namespace MIXO6.Services;

public sealed record TemperatureSnapshot(double? CpuCelsius, double? GpuCelsius, string CpuName, string GpuName, string Status);

/// <summary>Reads actual temperature sensors. Unsupported sensors are reported as unavailable, never guessed.</summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
    private bool _opened;

    public TemperatureSnapshot Read()
    {
        try
        {
            if (!_opened) { _computer.Open(); _opened = true; }
            double? cpu = null, gpu = null;
            var cpuScore = int.MinValue;
            var gpuScore = int.MinValue;
            var cpuName = "CPU sensor";
            var gpuName = "GPU sensor";
            foreach (var hardware in _computer.Hardware)
                ReadHardware(hardware, ref cpu, ref gpu, ref cpuScore, ref gpuScore, ref cpuName, ref gpuName);

            var status = cpu is null && gpu is null
                ? "دادهٔ دمای واقعی پیدا نشد. دکمهٔ «خواندن سنسورها با مجوز مدیر» را بزن؛ بعضی سازنده‌ها سنسور را فقط با دسترسی مدیر یا ابزار اختصاصی در اختیار می‌گذارند."
                : cpu is null ? "سنسور CPU خوانده نشد؛ برای امنیت هیچ دمای حدسی نمایش داده نمی‌شود."
                : gpu is null ? "سنسور GPU خوانده نشد؛ برای امنیت هیچ دمای حدسی نمایش داده نمی‌شود."
                : "دمای واقعی سنسورها در حال به‌روزرسانی است.";
            return new TemperatureSnapshot(cpu, gpu, cpuName, gpuName, status);
        }
        catch (Exception ex)
        {
            return new TemperatureSnapshot(null, null, "CPU", "GPU", "خواندن سنسورها ناموفق بود: " + ex.Message);
        }
    }

    private static void ReadHardware(IHardware hardware, ref double? cpu, ref double? gpu,
        ref int cpuScore, ref int gpuScore, ref string cpuName, ref string gpuName)
    {
        try { hardware.Update(); } catch { }
        var kind = hardware.HardwareType.ToString();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || sensor.Value is not float rawValue) continue;
            var value = (double)rawValue;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < -20 || value > 150) continue;

            var name = sensor.Name ?? "Temperature";
            var lowerName = name.ToLowerInvariant();
            var lowerKind = kind.ToLowerInvariant();
            var isCpuHardware = lowerKind.Contains("cpu", StringComparison.Ordinal);
            var isGpuHardware = lowerKind.Contains("gpu", StringComparison.Ordinal);
            var isCpuFallback = lowerKind.Contains("motherboard", StringComparison.Ordinal) &&
                                (lowerName.Contains("cpu", StringComparison.Ordinal) || lowerName.Contains("package", StringComparison.Ordinal) ||
                                 lowerName.Contains("tctl", StringComparison.Ordinal) || lowerName.Contains("tdie", StringComparison.Ordinal));
            var isGpuFallback = lowerKind.Contains("motherboard", StringComparison.Ordinal) &&
                                (lowerName.Contains("gpu", StringComparison.Ordinal) || lowerName.Contains("graphics", StringComparison.Ordinal));

            var score = SensorScore(name, isCpuHardware || isGpuHardware);
            if (isCpuHardware || isCpuFallback)
            {
                var adjusted = score + (isCpuHardware ? 100 : 0);
                if (adjusted > cpuScore) { cpu = value; cpuScore = adjusted; cpuName = hardware.Name + " · " + name; }
            }
            if (isGpuHardware || isGpuFallback)
            {
                var adjusted = score + (isGpuHardware ? 100 : 0);
                if (adjusted > gpuScore) { gpu = value; gpuScore = adjusted; gpuName = hardware.Name + " · " + name; }
            }
        }
        foreach (var sub in hardware.SubHardware)
            ReadHardware(sub, ref cpu, ref gpu, ref cpuScore, ref gpuScore, ref cpuName, ref gpuName);
    }

    private static int SensorScore(string name, bool primaryHardware)
    {
        var score = primaryHardware ? 10 : 0;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) score += 45;
        if (name.Contains("Core Average", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase)) score += 45;
        if (name.Contains("Junction", StringComparison.OrdinalIgnoreCase)) score += 35;
        if (name.Contains("Core", StringComparison.OrdinalIgnoreCase)) score += 15;
        return score;
    }

    public void Dispose()
    {
        if (_opened) { try { _computer.Close(); } catch { } _opened = false; }
    }
}
