using LibreHardwareMonitor.Hardware;

namespace MIXO6.Services;

public sealed record TemperatureSnapshot(double? CpuCelsius, double? GpuCelsius, string CpuName, string GpuName, string Status);

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
            string cpuName = "CPU sensor", gpuName = "GPU sensor";
            foreach (var hardware in _computer.Hardware) ReadHardware(hardware, ref cpu, ref gpu, ref cpuName, ref gpuName);
            var status = cpu is null && gpu is null ? "سنسور دما در این دستگاه در دسترس نیست" : "در حال خواندن سنسور واقعی";
            return new TemperatureSnapshot(cpu, gpu, cpuName, gpuName, status);
        }
        catch (Exception ex)
        {
            return new TemperatureSnapshot(null, null, "CPU", "GPU", "خواندن سنسورها ممکن نشد: " + ex.Message);
        }
    }

    private static void ReadHardware(IHardware hardware, ref double? cpu, ref double? gpu, ref string cpuName, ref string gpuName)
    {
        try { hardware.Update(); } catch { }
        var kind = hardware.HardwareType.ToString();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || sensor.Value is null) continue;
            var value = sensor.Value.Value;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < -20 || value > 150) continue;
            if (kind.Equals("Cpu", StringComparison.OrdinalIgnoreCase) || kind.Contains("Cpu", StringComparison.OrdinalIgnoreCase))
            {
                if (cpu is null || IsPreferred(sensor.Name)) { cpu = value; cpuName = hardware.Name + " · " + sensor.Name; }
            }
            else if (kind.Contains("Gpu", StringComparison.OrdinalIgnoreCase))
            {
                if (gpu is null || IsPreferred(sensor.Name)) { gpu = value; gpuName = hardware.Name + " · " + sensor.Name; }
            }
        }
        foreach (var sub in hardware.SubHardware) ReadHardware(sub, ref cpu, ref gpu, ref cpuName, ref gpuName);
    }

    private static bool IsPreferred(string name) => name.Contains("Package", StringComparison.OrdinalIgnoreCase) || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) || name.Contains("Core Average", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_opened) { try { _computer.Close(); } catch { } _opened = false; }
    }
}
