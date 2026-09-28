using System.IO.MemoryMappedFiles;
using System.Text;
using ThermalScope;
using Microsoft.Data.Sqlite;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
string memoryName = "ThermalScope-fixture-" + Guid.NewGuid().ToString("N");
using var map = MemoryMappedFile.CreateNew(memoryName, 32 + 2 * 1324);
using var view = map.CreateViewAccessor();
view.Write(0, 0x4D41484Du); view.Write(4, 0x20000u); view.Write(8, 32u); view.Write(12, 2u); view.Write(16, 1324u);
view.Write(20, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
void Entry(long offset, string name, string unit, float value, uint sourceId)
{
    byte[] n = Encoding.Latin1.GetBytes(name), u = Encoding.Latin1.GetBytes(unit);
    view.WriteArray(offset, n, 0, n.Length); view.WriteArray(offset + 260, u, 0, u.Length);
    view.Write(offset + 1300, value); view.Write(offset + 1316, uint.MaxValue); view.Write(offset + 1320, sourceId);
}
Entry(32, "CPU temperature", "C", 71.5f, 0x80); Entry(32 + 1324, "Framerate", "FPS", float.MaxValue, 0x50);
var readings = Afterburner.Read([memoryName]);
Check(readings.Length == 2 && readings[0].Value == 71.5 && readings[0].Unit == "°C", "Afterburner layout or unit parsing failed.");
Check(readings[1].Value is null, "Afterburner unavailable sentinel must be null.");
Check(SensorSource.AutomaticId("cpuTemp", readings) == readings[0].Id, "CPU fallback selection failed.");
view.Write(20, (int)DateTimeOffset.UtcNow.AddSeconds(-20).ToUnixTimeSeconds());
Check(Afterburner.Read([memoryName]).Length == 0, "Stale Afterburner readings must be rejected.");
view.Write(20, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); view.Write(12, uint.MaxValue);
Check(Afterburner.Read([memoryName]).Length == 0, "Corrupt entry count must be rejected.");
view.Write(12, 2u); view.Write(0, 0xDEADu);
Check(Afterburner.Read([memoryName]).Length == 0, "Deallocated shared memory must be rejected.");
var genericFan = new Reading("fan1", "Fan #1", "Board", "SuperIO", "Fan", "RPM", 2000, "LibreHardwareMonitor");
Check(SensorSource.AutomaticId("cpuFan", [genericFan]) is null, "Generic fan must not be assigned to CPU automatically.");
var gpuPercent = new Reading("gpuPct", "Fan tachometer", "MSI Afterburner", "Afterburner", "Load", "%", 50, "Afterburner");
Check(SensorSource.AutomaticId("gpuFan", [gpuPercent]) is null, "Fan percentage must not be treated as RPM.");
Frame F(int minute, double? temp) => new(minute, DateTimeOffset.UnixEpoch.AddMinutes(minute), new() { ["cpuTemp"] = temp }, [], [], true);
var summary = MetricCatalog.Summarize([F(0, 40), F(30, 60), F(50, null), F(55, 80), F(60, 100)])["cpuTemp"];
Check(summary.Count == 4 && summary.Average == 70 && summary.Peak == 100 && summary.Final15Average == 90, "Session statistics/window/null filtering failed.");
Check(MetricCatalog.Summarize([])["cpuTemp"].Average is null, "Empty session should not average to zero.");
Check(MetricCatalog.Summarize([F(0, 0)])["cpuTemp"].Average == 0, "Zero is a valid reading.");
var storagePath = Path.Combine(Path.GetTempPath(), "ThermalScope-core-" + Guid.NewGuid().ToString("N"));
using (var store = new SessionStore(storagePath))
{
    var session = new Session("compact-check", "Compact transfer", "Wraith Prism", "", null, "", DateTimeOffset.UtcNow, null, 60, "recording", 0);
    store.Save(session);
    var rawFrame = new Frame(1, DateTimeOffset.UtcNow, new() { ["cpuTemp"] = 71.5 }, readings, [], true);
    store.Append(session.Id, rawFrame);
    var compact = store.ReadFrames(session.Id);
    Check(compact.Length == 1 && compact[0].Metrics["cpuTemp"] == 71.5 && compact[0].Sensors.Length == 0, "Chart transfer should preserve metrics without repeated sensor metadata.");
    using var db = new SqliteConnection($"Data Source={Path.Combine(storagePath, "sessions.sqlite")}"); db.Open();
    using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM frames WHERE session_id='compact-check'";
    var persisted = System.Text.Json.JsonSerializer.Deserialize<Frame>((string)cmd.ExecuteScalar()!, SessionStore.Json)!;
    Check(persisted.Sensors.Length == 2 && persisted.Sensors[0].Value == 71.5, "Raw sensor readings must remain in SQLite.");
}
var portablePath = Path.Combine(storagePath, "portable", "app");
Directory.CreateDirectory(portablePath);
Check(DataDirectories.Default(portablePath, false) == Path.GetFullPath(Path.Combine(portablePath, "..", "data")), "Portable recordings must remain beside the app.");
File.WriteAllText(Path.Combine(portablePath, "..", "installed.flag"), "installed");
Check(DataDirectories.Default(portablePath, false) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalScope", "data"), "Installed recordings must use writable user storage.");
Check(DataDirectories.Default(portablePath, true).EndsWith("data-demo"), "Installed demo recordings must be isolated from real sessions.");
Console.WriteLine($"Core checks passed: {checks}.");
