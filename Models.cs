namespace ThermalScope;

public record Reading(string Id, string Name, string Hardware, string HardwareType, string Type, string Unit, double? Value, string Source);
public record Frame(long Sequence, DateTimeOffset Time, Dictionary<string, double?> Metrics, Reading[] Sensors, string[] Warnings, bool Demo);
public record Session(string Id, string Name, string Cooler, string Game, double? Ambient, string Notes, DateTimeOffset Started, DateTimeOffset? Ended, int Minutes, string Status, int Samples);
public record StartRequest(string Name, string Cooler, string Game, double? Ambient, string Notes, int Minutes = 60);
public record MappingRequest(Dictionary<string, string> Mappings);
public record MetricSummary(int Count, double? Average, double? Peak, double? Final15Average);
public record SessionDetail(Session Session, Frame[] Frames, Dictionary<string, MetricSummary> Summary);

public static class MetricCatalog
{
    public static readonly Dictionary<string, string> Units = new()
    {
        ["cpuTemp"] = "°C", ["gpuTemp"] = "°C", ["gpuHotspot"] = "°C",
        ["cpuFan"] = "RPM", ["gpuFan"] = "RPM", ["pumpFan"] = "RPM", ["systemFans"] = "RPM",
        ["cpuPower"] = "W", ["gpuPower"] = "W", ["cpuLoad"] = "%", ["gpuLoad"] = "%",
        ["ramUsed"] = "GB", ["fps"] = "FPS", ["frameTime"] = "ms"
    };

    public static Dictionary<string, MetricSummary> Summarize(Frame[] frames)
    {
        // Final 15 minutes of the actual recorded interval; short sessions use all available samples.
        var cutoff = frames.Length == 0 ? DateTimeOffset.MaxValue : frames[^1].Time.AddMinutes(-15);
        return Units.Keys.ToDictionary(key => key, key =>
        {
            var values = frames.Select(f => f.Metrics.GetValueOrDefault(key)).OfType<double>().ToArray();
            var last = frames.Where(f => f.Time >= cutoff).Select(f => f.Metrics.GetValueOrDefault(key)).OfType<double>().ToArray();
            return new MetricSummary(values.Length, values.Length > 0 ? values.Average() : null,
                values.Length > 0 ? values.Max() : null, last.Length > 0 ? last.Average() : null);
        });
    }
}
