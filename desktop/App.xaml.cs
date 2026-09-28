using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ThermalScope.Desktop;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = LauncherOptions.Parse(e.Args);
        instance = new Mutex(true, "Local\\ThermalScope.Desktop." + options.Port, out bool created);
        if (!created)
        {
            foreach (var process in Process.GetProcessesByName("ThermalScope.Desktop"))
                if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero)
                { ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); break; }
            Shutdown(); return;
        }
        MainWindow = new MainWindow(options);
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        (MainWindow as MainWindow)?.ReleaseServer();
        instance?.Dispose(); base.OnExit(e);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}

public record LauncherOptions(bool Demo, int Port, string? DataDirectory, string? SelfTestDirectory)
{
    public static LauncherOptions Parse(string[] args)
    {
        string? Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool demo = args.Contains("--demo");
        return new(demo, int.TryParse(Value("--port"), out int port) ? port : demo ? 18099 : 8088,
            Value("--data-dir") ?? ThermalScope.DataDirectories.Default(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), demo), Value("--self-test"));
    }
}
