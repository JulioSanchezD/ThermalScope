using LibreHardwareMonitor.Hardware;
using System.Security.Principal;

namespace ThermalScope;

public sealed class SensorSource(bool demo) : IDisposable
{
    private static readonly bool RegisterAccess = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled &&
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private Computer? computer;
    private string? initError;
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;

    public void Open()
    {
        if (demo) return;
        computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
        try { computer.Open(); }
        catch (Exception ex) { initError = $"Sensor initialization: {ex.Message}"; }
    }

    public (Reading[] Readings, string[] Warnings) Read()
    {
        if (demo) return (Demo(), ["DEMO MODE — simulated readings, not your PC's temperatures."]);
        var values = new List<Reading>();
        var warnings = new List<string>();
        if (initError is not null) warnings.Add(initError);
        if (!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)
            warnings.Add("PawnIO sensor driver is not installed. CPU temperatures, CPU power and motherboard readings are unavailable.");
        if (computer is not null)
            foreach (var hardware in computer.Hardware) Collect(hardware, values, warnings);
        values.AddRange(Afterburner.Read());
        if (!values.Any(r => r.HardwareType == "Cpu" && r.Type == "Temperature" && r.Value.HasValue))
            warnings.Add("CPU temperature unavailable. Run ThermalScope as administrator and check that the official PawnIO sensor driver is installed.");
        if (!values.Any(r => r.Type == "Fan" && r.HardwareType != "GpuNvidia" && r.HardwareType != "GpuAmd" && r.Source != "Afterburner"))
            warnings.Add("Motherboard fan RPM is unavailable. Driver access or motherboard support may be needed.");
        return (values.ToArray(), warnings.Distinct().ToArray());
    }

    private static void Collect(IHardware hardware, List<Reading> readings, List<string> warnings)
    {
        try
        {
            hardware.Update();
            foreach (var s in hardware.Sensors)
            {
                if (s.SensorType is not (SensorType.Temperature or SensorType.Fan or SensorType.Power or SensorType.Load or SensorType.Data or SensorType.SmallData)) continue;
                var unit = s.SensorType switch { SensorType.Temperature => "°C", SensorType.Fan => "RPM", SensorType.Power => "W", SensorType.Load => "%", SensorType.Data => "GB", SensorType.SmallData => "MB", _ => "" };
                double? value = s.Value is float f && float.IsFinite(f) ? f : null;
                // The library may expose zero-valued AMD register readings when its driver is absent.
                // Preserve genuine zero RPM, but never present inaccessible CPU registers as measurements.
                if (!RegisterAccess &&
                    ((hardware.HardwareType == HardwareType.Cpu && s.SensorType is SensorType.Temperature or SensorType.Power) || hardware.HardwareType == HardwareType.SuperIO))
                    value = null;
                readings.Add(new(s.Identifier.ToString(), s.Name, hardware.Name, hardware.HardwareType.ToString(), s.SensorType.ToString(), unit, value, "LibreHardwareMonitor"));
            }
        }
        catch (Exception ex) { warnings.Add($"{hardware.Name}: {ex.Message}"); }
        foreach (var child in hardware.SubHardware) Collect(child, readings, warnings);
    }

    public static string? AutomaticId(string key, Reading[] readings)
    {
        Reading? Find(string hw, string type, params string[] names) => names.Select(name => readings.FirstOrDefault(r => r.HardwareType == hw && r.Type == type && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(r => r is not null);
        var primary = key switch
        {
            "cpuTemp" => Find("Cpu", "Temperature", "Core (Tctl/Tdie)", "CPU Package", "Core (Tdie)", "CPU CCD1 (Tdie)"),
            "cpuPower" => Find("Cpu", "Power", "Package", "CPU Package", "Package Power"),
            "cpuLoad" => Find("Cpu", "Load", "CPU Total"),
            "gpuTemp" => Find("GpuNvidia", "Temperature", "GPU Core"),
            "gpuHotspot" => Find("GpuNvidia", "Temperature", "GPU Hot Spot", "GPU Hotspot"),
            "gpuPower" => Find("GpuNvidia", "Power", "GPU Package", "GPU Power", "GPU Board"),
            "gpuLoad" => Find("GpuNvidia", "Load", "GPU Core"),
            "gpuFan" => readings.FirstOrDefault(r => r.HardwareType == "GpuNvidia" && r.Type == "Fan"),
            "ramUsed" => Find("Memory", "Data", "Memory Used"),
            // Only select an explicitly named CPU/pump fan. Generic Fan #1 is deliberately not guessed.
            "cpuFan" => readings.FirstOrDefault(r => r.Source != "Afterburner" && r.Type == "Fan" && r.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)),
            "pumpFan" => readings.FirstOrDefault(r => r.Source != "Afterburner" && r.Type == "Fan" && !r.Name.Contains('/') && r.Name.Contains("Pump", StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
        if (primary is not null) return primary.Id;
        string[] fallbackNames = key switch
        {
            "cpuTemp" => ["CPU temperature"], "gpuTemp" => ["GPU temperature"], "cpuPower" => ["CPU power"],
            "gpuPower" => ["GPU power"], "cpuLoad" => ["CPU usage"], "gpuLoad" => ["GPU usage"],
            "gpuFan" => ["Fan tachometer", "Fan tachometer 1"], "fps" => ["Framerate"], "frameTime" => ["Frametime"], _ => []
        };
        return readings.FirstOrDefault(r => r.Source == "Afterburner" && r.Unit == MetricCatalog.Units[key] && fallbackNames.Any(n => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))?.Id;
    }

    public static double? SystemFanAverage(Reading[] readings, Dictionary<string, string> mappings)
    {
        var excluded = new[] { "cpuFan", "gpuFan", "pumpFan" }
            .Select(key => mappings.TryGetValue(key, out var assigned) ? assigned : AutomaticId(key, readings))
            .Where(id => id is not null).ToHashSet();
        var fans = readings.Where(r => r.Type == "Fan" && r.Unit == "RPM" && r.Source != "Afterburner"
            && !r.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)
            && !excluded.Contains(r.Id)
            && !new[] { "CPU", "GPU", "Pump" }.Any(role => r.Name.Contains(role, StringComparison.OrdinalIgnoreCase))
            && r.Value.HasValue && double.IsFinite(r.Value.Value) && r.Value.Value >= 0)
            .DistinctBy(r => r.Id).Select(r => r.Value!.Value).ToArray();
        return fans.Length > 0 ? fans.Average() : null;
    }

    private Reading[] Demo()
    {
        double t = (DateTimeOffset.UtcNow - started).TotalSeconds;
        Reading R(string id, string name, string hw, string type, string unit, double value) => new(id, name, hw == "Cpu" ? "Example CPU (demo)" : hw == "GpuNvidia" ? "Example GPU (demo)" : "Example motherboard (demo)", hw, type, unit, value, "Demo");
        return [R("demo/cpu/temp", "Core (Tctl/Tdie)", "Cpu", "Temperature", "°C", 65 + 7 * Math.Sin(t / 19)),
            R("demo/gpu/temp", "GPU Core", "GpuNvidia", "Temperature", "°C", 60 + 4 * Math.Sin(t / 25)),
            R("demo/gpu/hotspot", "GPU Hot Spot", "GpuNvidia", "Temperature", "°C", 72 + 4 * Math.Sin(t / 25)),
            R("demo/cpu/power", "Package", "Cpu", "Power", "W", 70 + 12 * Math.Sin(t / 19)),
            R("demo/gpu/power", "GPU Package", "GpuNvidia", "Power", "W", 220 + 20 * Math.Sin(t / 25)),
            R("demo/cpu/load", "CPU Total", "Cpu", "Load", "%", 45 + 8 * Math.Sin(t / 11)),
            R("demo/gpu/load", "GPU Core", "GpuNvidia", "Load", "%", 95 + 3 * Math.Sin(t / 11)),
            R("demo/cpu/fan", "CPU Fan", "SuperIO", "Fan", "RPM", 2100 + 220 * Math.Sin(t / 19)),
            R("demo/gpu/fan", "GPU", "GpuNvidia", "Fan", "RPM", 1300 + 120 * Math.Sin(t / 25)),
            R("demo/system/fan1", "System Fan #1", "SuperIO", "Fan", "RPM", 1000 + 80 * Math.Sin(t / 19)),
            R("demo/system/fan2", "System Fan #2", "SuperIO", "Fan", "RPM", 1200 + 100 * Math.Sin(t / 19))];
    }

    public void Dispose() { try { computer?.Close(); } catch { } }
}
