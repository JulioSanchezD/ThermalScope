using System.Diagnostics;

namespace ThermalScope;

public sealed class MonitorService(SessionStore store, bool demo, ILogger<MonitorService> logger) : BackgroundService
{
    private readonly object gate = new();
    private readonly Queue<Frame> history = new();
    private Dictionary<string, string> mappings = store.GetMappings();
    private Frame? latest;
    private Session? active;
    private long startedTick;
    private long sequence;
    private string? recordingError;

    public object Live()
    {
        lock (gate) return new { Frame = latest, Active = active, RecordingError = recordingError, ServerTime = DateTimeOffset.UtcNow };
    }
    public Frame[] History() { lock (gate) return history.ToArray(); }
    public object Settings()
    {
        lock (gate) return new { Mappings = mappings, Resolved = MetricCatalog.Units.Keys.ToDictionary(k => k, k => mappings.TryGetValue(k, out var id) ? id : SensorSource.AutomaticId(k, latest?.Sensors ?? [])), Units = MetricCatalog.Units };
    }
    public void Map(Dictionary<string, string> requested)
    {
        lock (gate)
        {
            if (active is not null) throw new InvalidOperationException("Stop recording before changing sensor assignments.");
            if (requested is null) throw new ArgumentException("Mappings are required.");
            if (requested.ContainsKey("systemFans")) throw new ArgumentException("System fans is a calculated average, not a single sensor assignment.");
            if (requested.Keys.Any(k => !MetricCatalog.Units.ContainsKey(k))) throw new ArgumentException("Unknown metric.");
            foreach (var (key, id) in requested)
                if (id is null || (id.Length > 0 && !((latest?.Sensors ?? []).Any(r => r.Id == id && r.Unit == MetricCatalog.Units[key]))))
                    throw new ArgumentException($"The sensor selected for {key} has an incompatible unit or is unavailable.");
            var updated = requested.Where(p => p.Value.Length > 0).ToDictionary(p => p.Key, p => p.Value);
            store.SetMappings(updated); mappings = updated;
        }
    }

    public Session Start(StartRequest request)
    {
        lock (gate)
        {
            if (active is not null) throw new InvalidOperationException("A session is already recording.");
            if (latest is null || (DateTimeOffset.UtcNow - latest.Time).TotalSeconds > 10 || !latest.Metrics.Values.Any(v => v.HasValue)) throw new InvalidOperationException("Wait for live sensor readings before recording.");
            if (request.Minutes is < 1 or > 240 || request.Ambient is < -20 or > 60) throw new ArgumentException("Duration must be 1–240 minutes and room temperature must be −20 to 60 °C.");
            if (request.Name is null || request.Cooler is null || request.Game is null || request.Notes is null || request.Name.Length > 120 || request.Cooler.Length > 120 || request.Game.Length > 120 || request.Notes.Length > 2000) throw new ArgumentException("Session fields are too long or missing.");
            var name = string.IsNullOrWhiteSpace(request.Name) ? $"Session {DateTimeOffset.Now:yyyy-MM-dd HH:mm}" : request.Name.Trim();
            var session = new Session(Guid.NewGuid().ToString("N"), name, request.Cooler, request.Game, request.Ambient, request.Notes, DateTimeOffset.UtcNow, null, request.Minutes, "recording", 0);
            store.Save(session); active = session; recordingError = null; startedTick = Stopwatch.GetTimestamp(); return session;
        }
    }
    public Session? Stop(string status = "stopped")
    {
        lock (gate)
        {
            if (active is null) return null;
            var result = active with { Status = status, Ended = latest?.Time ?? DateTimeOffset.UtcNow };
            store.Save(result); active = null; return result;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Driver discovery can take seconds; keep it off the web server startup thread.
        await Task.Yield();
        using var source = new SensorSource(demo);
        source.Open();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    var (readings, warnings) = source.Read();
                    lock (gate)
                    {
                        var metrics = MetricCatalog.Units.Keys.ToDictionary(k => k, k =>
                        {
                            if (k == "systemFans") return SensorSource.SystemFanAverage(readings, mappings);
                            var id = mappings.TryGetValue(k, out var assigned) ? assigned : SensorSource.AutomaticId(k, readings);
                            return readings.FirstOrDefault(r => r.Id == id)?.Value;
                        });
                        latest = new Frame(++sequence, DateTimeOffset.UtcNow, metrics, readings, warnings, demo);
                        // History charts need metrics, not thousands of repeated sensor descriptions.
                        history.Enqueue(latest with { Sensors = [] }); while (history.Count > 7200) history.Dequeue();
                        if (active is not null)
                        {
                            try
                            {
                                store.Append(active.Id, latest);
                                active = active with { Samples = active.Samples + 1 };
                                if (Stopwatch.GetElapsedTime(startedTick).TotalMinutes >= active.Minutes) Stop("completed");
                            }
                            catch (Exception ex)
                            {
                                recordingError = $"Recording stopped: {ex.Message}. Previously saved samples remain on disk.";
                                logger.LogError(ex, "Recording failed");
                                // Keep serving readings even if the disk becomes full.
                                try { Stop("error"); } catch { active = null; }
                            }
                        }
                    }
                }
                catch (Exception ex) { logger.LogError(ex, "Sensor poll failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { try { Stop("stopped"); } catch (Exception ex) { logger.LogError(ex, "Finalizing session failed"); } }
    }
}
