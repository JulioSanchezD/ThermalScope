using System.IO;

namespace ThermalScope;

public static class DataDirectories
{
    // Installed programs must never write sessions inside Program Files.
    // Portable/development builds retain their existing adjacent data folder.
    public static string Default(string serverDirectory, bool demo) =>
        File.Exists(Path.Combine(serverDirectory, "..", "installed.flag"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalScope", demo ? "data-demo" : "data")
            : Path.GetFullPath(Path.Combine(serverDirectory, "..", demo ? "data-demo" : "data"));
}
