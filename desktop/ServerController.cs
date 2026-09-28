using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ThermalScope.Desktop;

public sealed class ServerController(LauncherOptions options) : IDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    private readonly ConcurrentQueue<string> diagnostics = new();
    private Process? server;
    private ServerJob? job;
    private string? pipeName;
    private CancellationTokenSource? starting;
    public string LocalUrl => $"http://localhost:{options.Port}";
    private string ApiUrl => $"http://127.0.0.1:{options.Port}";
    public bool IsRunning => server is { HasExited: false };
    public int? ProcessId => IsRunning ? server!.Id : null;

    public async Task<JsonElement> StartAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (IsRunning) return await InfoAsync();
            KillOwnedServer();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30)); starting = startup;
            string exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ThermalScope.exe"));
            if (!File.Exists(exe)) throw new FileNotFoundException("The ThermalScope server executable is missing. Rebuild the application.");
            // An existing server belongs to its original window; never attach to or stop it accidentally.
            try
            {
                using var probe = await http.GetAsync(ApiUrl + "/api/info", startup.Token);
                throw new InvalidOperationException($"Port {options.Port} is already in use. Close the existing server window, then press Start server.");
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!startup.IsCancellationRequested) { }
            pipeName = "ThermalScope.shutdown." + Guid.NewGuid().ToString("N");
            var launch = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.ArgumentList.Add("--port"); launch.ArgumentList.Add(options.Port.ToString());
            launch.ArgumentList.Add("--control-pipe"); launch.ArgumentList.Add(pipeName);
            if (options.Demo) launch.ArgumentList.Add("--demo");
            if (options.DataDirectory is string data) { launch.ArgumentList.Add("--data-dir"); launch.ArgumentList.Add(data); }
            diagnostics.Clear(); job = new ServerJob();
            var child = new Process { StartInfo = launch };
            child.OutputDataReceived += (_, e) => Log(e.Data); child.ErrorDataReceived += (_, e) => Log(e.Data);
            try { if (!child.Start()) throw new InvalidOperationException("The server could not start."); }
            catch { child.Dispose(); throw; }
            server = child;
            job.Own(server); server.BeginOutputReadLine(); server.BeginErrorReadLine();
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (server.HasExited) throw new InvalidOperationException("The server exited during startup. " + string.Join(" ", diagnostics.TakeLast(4)));
                try
                {
                    var info = await http.GetFromJsonAsync<JsonElement>(ApiUrl + "/api/info", startup.Token);
                    if (info.GetProperty("processId").GetInt32() != server.Id) throw new InvalidOperationException("Another application is already using the server port.");
                    return info;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!startup.IsCancellationRequested) { }
                await Task.Delay(200, startup.Token);
            }
        }
        catch
        {
            KillOwnedServer(); throw;
        }
        finally { starting = null; lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        starting?.Cancel();
        await lifecycle.WaitAsync();
        try
        {
            if (!IsRunning) { KillOwnedServer(); return; }
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName!, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(1500);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes("stop\n")); await pipe.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await server!.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
            finally { KillOwnedServer(); }
        }
        finally { lifecycle.Release(); }
    }
    public Task<JsonElement> InfoAsync() => http.GetFromJsonAsync<JsonElement>(ApiUrl + "/api/info");
    public Task<JsonElement> LiveAsync() => http.GetFromJsonAsync<JsonElement>(ApiUrl + "/api/live");
    public async Task<JsonElement> PostAsync(string path, object body)
    {
        using var response = await http.PostAsJsonAsync(ApiUrl + path, body); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public Task<JsonElement> GetAsync(string path) => http.GetFromJsonAsync<JsonElement>(ApiUrl + path);
    private void Log(string? line) { if (string.IsNullOrWhiteSpace(line)) return; diagnostics.Enqueue(line); while (diagnostics.Count > 20) diagnostics.TryDequeue(out _); }
    private void KillOwnedServer()
    {
        try { if (server is { HasExited: false }) { server.Kill(entireProcessTree: true); server.WaitForExit(3000); } }
        finally { server?.Dispose(); server = null; job?.Dispose(); job = null; }
    }
    public void Dispose() { KillOwnedServer(); http.Dispose(); }
}
