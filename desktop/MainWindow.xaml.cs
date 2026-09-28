using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ThermalScope.Desktop;

public partial class MainWindow : Window
{
    private readonly LauncherOptions options;
    private readonly ServerController controller;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool busy, polling, closing, closeAllowed;
    private string[] addresses = [];
    private readonly List<string> checks = [];
    private string? testError;

    public MainWindow(LauncherOptions options)
    {
        this.options = options; controller = new ServerController(options);
        InitializeComponent(); LocalAddress.Text = controller.LocalUrl;
        timer.Tick += async (_, _) => await RefreshAsync();
        if (options.Demo) Footnote.Text = "DEMO MODE  ·  SIMULATED READINGS";
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1, rounded = 2;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        await StartAsync(); timer.Start();
        if (options.SelfTestDirectory is not null) await RunSelfTestAsync();
    }
    private void Status(string title, string detail, string color)
    {
        StatusTitle.Text = title; StatusDetail.Text = detail;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
    private void Controls()
    {
        bool running = controller.IsRunning;
        StartStop.IsEnabled = !busy && !closing;
        StartStop.Content = busy ? (running ? "Stopping…" : "Starting…") : running ? "■  Stop server" : "▶  Start server";
        StartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#1D303F" : "#6BE1BB"));
        StartStop.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#D8E7F0" : "#10251E"));
        StartStop.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#334B5D" : "#6BE1BB"));
        OpenLocal.IsEnabled = running && !busy && !closing;
        CopyAddress.IsEnabled = addresses.Length > 0 && !closing;
    }
    private async Task StartAsync()
    {
        busy = true; Controls(); Status("Starting server…", "Preparing your dashboard.", "#FFB566");
        try { UpdateAddresses(await controller.StartAsync()); Status("Server running", "Live data is available on your home network.", "#6BE1BB"); }
        catch (OperationCanceledException) { Status(closing ? "Server stopped" : "Server startup timed out", closing ? "Monitoring has stopped." : "The server did not respond in time. Press Start server to retry.", "#FFB566"); }
        catch (Exception ex) { Status("Couldn't start server", ex.Message, "#FFAAA1"); }
        finally { busy = false; Controls(); }
    }
    private async Task StopAsync()
    {
        busy = true; Controls(); Status("Stopping server…", "Saving recordings and shutting down.", "#FFB566");
        try { await controller.StopAsync(); Status("Server stopped", "Press Start server to resume monitoring.", "#839AAA"); RecordingLabel.Visibility = Visibility.Collapsed; }
        catch (Exception ex) { Status("Server stopped", ex.Message, "#FFAAA1"); controller.Dispose(); }
        finally { busy = false; Controls(); }
    }
    private void UpdateAddresses(JsonElement info)
    {
        addresses = info.GetProperty("addresses").EnumerateArray().Select(a => a.GetString()!).ToArray();
        PhoneAddress.Text = addresses.FirstOrDefault() ?? "No LAN connection detected";
        ExtraAddresses.Text = string.Join("  ·  ", addresses.Skip(1));
        ExtraAddresses.Visibility = addresses.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task RefreshAsync()
    {
        if (busy || polling || closing) return;
        if (!controller.IsRunning) { Status("Server stopped", "Press Start server to resume monitoring.", "#839AAA"); RecordingLabel.Visibility = Visibility.Collapsed; Controls(); return; }
        polling = true;
        try
        {
            var live = await controller.LiveAsync();
            if (closing || busy) return;
            if (live.GetProperty("active").ValueKind != JsonValueKind.Null)
            {
                var active = live.GetProperty("active");
                RecordingLabel.Text = "● Recording · " + active.GetProperty("name").GetString();
                RecordingLabel.Visibility = Visibility.Visible;
            }
            else RecordingLabel.Visibility = Visibility.Collapsed;
            Status("Server running", "Live data is available on your home network.", "#6BE1BB");
            UpdateAddresses(await controller.InfoAsync()); Controls();
        }
        catch (Exception ex) { if (!closing && !busy) Status("Server not responding", ex.Message, "#FFB566"); }
        finally { polling = false; }
    }
    private async void StartStopClicked(object sender, RoutedEventArgs e) { if (busy || closing) return; if (controller.IsRunning) await StopAsync(); else await StartAsync(); }
    private void OpenLocalClicked(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(controller.LocalUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Status("Couldn't open browser", ex.Message, "#FFAAA1"); }
    }
    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        if (addresses.Length == 0) return;
        try { Clipboard.SetText(addresses[0]); CopyAddress.Content = "Copied ✓"; }
        catch (ExternalException) { CopyAddress.Content = "Try again"; }
    }
    private void MinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true; if (closing) return;
        closing = true; timer.Stop(); Controls();
        try { await StopAsync(); }
        finally
        {
            if (options.SelfTestDirectory is string output)
            {
                if (!controller.IsRunning) checks.Add("Closing the window terminates the owned server");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "launcher-checks.json"), JsonSerializer.Serialize(new { Passed = testError is null, Checks = checks, Error = testError }, new JsonSerializerOptions { WriteIndented = true }));
            }
            closeAllowed = true; Close();
        }
    }
    public void ReleaseServer() => controller.Dispose();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private async Task RunSelfTestAsync()
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        try
        {
            Check(controller.IsRunning && OpenLocal.IsEnabled, "Server starts automatically and dashboard button is enabled");
            Check(addresses.Length > 0 && CopyAddress.IsEnabled, "LAN address is shown and copy control is enabled");
            // Wait for the collector, independently of web-server startup.
            for (int i = 0; i < 30; i++) { var live = await controller.LiveAsync(); if (live.GetProperty("frame").ValueKind != JsonValueKind.Null) break; await Task.Delay(200); }
            await Task.Delay(400); Screenshot("launcher-running.png");
            var session = await controller.PostAsync("/api/sessions/start", new { name = "Desktop lifecycle check", cooler = "Wraith Prism", game = "", ambient = (double?)null, notes = "isolated launcher test", minutes = 60 });
            string id = session.GetProperty("id").GetString()!;
            await Task.Delay(2300); await StopAsync();
            Check(!controller.IsRunning && !OpenLocal.IsEnabled && StartStop.IsEnabled, "Stop button terminates the server and keeps Start available");
            Screenshot("launcher-stopped.png");
            await StartAsync();
            Check(controller.IsRunning && OpenLocal.IsEnabled, "Server restarts from the same control window");
            var saved = await controller.GetAsync("/api/sessions/" + id);
            Check(saved.GetProperty("session").GetProperty("status").GetString() == "stopped" && saved.GetProperty("frames").GetArrayLength() >= 2, "Stopping the server gracefully saves an active recording");
            // Leave a second active recording for the actual window-close path.
            await controller.PostAsync("/api/sessions/start", new { name = "Window-close check", cooler = "Wraith Prism", game = "", ambient = (double?)null, notes = "isolated launcher test", minutes = 60 });
            await Task.Delay(2200);
        }
        catch (Exception ex) { testError = ex + "\nWindow status: " + StatusTitle.Text + " — " + StatusDetail.Text; Screenshot("launcher-error.png"); }
        finally { Close(); }
    }
    private void Screenshot(string file)
    {
        if (options.SelfTestDirectory is not string output) return;
        Directory.CreateDirectory(output); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, file)); encoder.Save(stream);
    }
}
