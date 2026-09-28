using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.IO.Pipes;
using ThermalScope;

string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool demo = args.Contains("--demo"), discover = args.Contains("--discover");
int port = int.TryParse(Arg("--port"), out int customPort) ? customPort : demo ? 8099 : 8088;
string dataPath = Path.GetFullPath(Arg("--data-dir") ?? DataDirectories.Default(AppContext.BaseDirectory, demo));
if (discover)
{
    using var source = new SensorSource(demo); source.Open(); Thread.Sleep(1200);
    var (readings, warnings) = source.Read();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Sensors = readings, Warnings = warnings }, SessionStore.Json));
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton(new SessionStore(dataPath));
builder.Services.AddSingleton(sp => new MonitorService(sp.GetRequiredService<SessionStore>(), demo, sp.GetRequiredService<ILogger<MonitorService>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorService>());
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (ctx.Request.Method == "POST")
    {
        string origin = ctx.Request.Headers.Origin.ToString();
        if ((origin.Length > 0 && origin != $"http://{ctx.Request.Host}") || !ctx.Request.HasJsonContentType())
        { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { Error = "Only same-origin JSON requests are accepted." }); return; }
    }
    try { await next(); }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    { ctx.Response.StatusCode = ex is InvalidOperationException ? 409 : 400; await ctx.Response.WriteAsJsonAsync(new { Error = ex.Message }); }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/live", (MonitorService monitor) => monitor.Live());
app.MapGet("/api/history", (MonitorService monitor) => monitor.History());
app.MapGet("/api/settings", (MonitorService monitor) => monitor.Settings());
app.MapPost("/api/settings", (MappingRequest req, MonitorService monitor) => { monitor.Map(req.Mappings); return Results.Ok(monitor.Settings()); });
app.MapGet("/api/sessions", (SessionStore store) => store.List());
app.MapPost("/api/sessions/start", (StartRequest req, MonitorService monitor) => Results.Ok(monitor.Start(req)));
app.MapPost("/api/sessions/stop", (MonitorService monitor) => Results.Ok(monitor.Stop()));
app.MapGet("/api/sessions/{id}", (string id, SessionStore store) =>
{
    var session = store.List().FirstOrDefault(s => s.Id == id); if (session is null) return Results.NotFound();
    var frames = store.ReadFrames(id); return Results.Ok(new SessionDetail(session, frames, MetricCatalog.Summarize(frames)));
});
app.MapGet("/api/sessions/{id}/csv", (string id, SessionStore store) =>
{
    if (!store.List().Any(s => s.Id == id)) return Results.NotFound();
    var frames = store.ReadFrames(id); var keys = MetricCatalog.Units.Keys.ToArray();
    var csv = new StringBuilder("timestamp_utc,elapsed_seconds," + string.Join(',', keys.Select(k => $"{k}_{MetricCatalog.Units[k].Replace("°", "").Replace("%", "percent")}")) + "\r\n");
    foreach (var f in frames)
    {
        csv.Append(f.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append((f.Time - (frames.FirstOrDefault()?.Time ?? f.Time)).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        foreach (var key in keys) csv.Append(',').Append(f.Metrics.GetValueOrDefault(key)?.ToString("F3", CultureInfo.InvariantCulture));
        csv.Append("\r\n");
    }
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"thermalscope-{id}.csv");
});
app.MapGet("/api/info", () => new { Name = "ThermalScope", Demo = demo, Port = port, Addresses = Addresses(port), DataPath = dataPath, ProcessId = Environment.ProcessId });
if (Arg("--control-pipe") is string pipeName)
{
    // Local, current-user-only shutdown channel; no server-control endpoint is exposed on the LAN.
    _ = Task.Run(async () =>
    {
        try
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(app.Lifetime.ApplicationStopping);
            using var reader = new StreamReader(pipe);
            if (await reader.ReadLineAsync(app.Lifetime.ApplicationStopping) == "stop") app.Lifetime.StopApplication();
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { app.Logger.LogWarning(ex, "Local shutdown channel closed"); }
    });
}
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"\n  THERMALSCOPE {(demo ? "[DEMO]" : "")}\n  PC: http://localhost:{port}\n  Phone: {string.Join("\n         ", Addresses(port))}\n  Recordings: {dataPath}\n  Close this window or press Ctrl+C to stop.\n");
    if (args.Contains("--open-browser"))
        try { Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine($"Open the address above in your browser. {ex.Message}"); }
});
await app.RunAsync();

static string[] Addresses(int port) => NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) && !n.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
    .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
    .Where(a => !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
    .Select(a => $"http://{a.Address}:{port}").Distinct().ToArray();
