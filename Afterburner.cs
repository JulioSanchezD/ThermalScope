using System.IO.MemoryMappedFiles;
using System.Text;

namespace ThermalScope;

// Layout is documented in the locally installed MSI Afterburner SDK's MAHMSharedMemory.h.
// Read-only monitoring interface; never opens the hardware control interface.
public static class Afterburner
{
    public static Reading[] Read(string[]? memoryNames = null)
    {
        foreach (var name in memoryNames ?? ["MAHMSharedMemory", "Global\\MAHMSharedMemory"])
        {
            try
            {
                using var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                if (view.Capacity < 32 || view.ReadUInt32(0) != 0x4D41484D || view.ReadUInt32(4) < 0x20000) continue;
                uint header = view.ReadUInt32(8), count = view.ReadUInt32(12), size = view.ReadUInt32(16);
                var polled = DateTimeOffset.FromUnixTimeSeconds(view.ReadInt32(20));
                if (Math.Abs((DateTimeOffset.UtcNow - polled).TotalSeconds) > 10) return [];
                if (header < 32 || size < 1324 || count > 4096 || (long)header + (long)count * size > view.Capacity) return [];
                var readings = new List<Reading>();
                for (uint i = 0; i < count; i++)
                {
                    long offset = header + (long)i * size;
                    var sensorName = ReadText(view, offset);
                    var unit = ReadText(view, offset + 260);
                    var value = view.ReadSingle(offset + 1300);
                    uint gpu = view.ReadUInt32(offset + 1316), sourceId = view.ReadUInt32(offset + 1320);
                    unit = unit switch { "C" or "°C" => "°C", "rpm" => "RPM", _ => unit };
                    var type = unit switch { "°C" => "Temperature", "RPM" => "Fan", "W" => "Power", "%" => "Load", "FPS" => "FPS", "ms" => "Time", _ => "Other" };
                    readings.Add(new($"ab/{gpu}/{sourceId}/{i}", sensorName, "MSI Afterburner", "Afterburner", type, unit,
                        float.IsFinite(value) && value != float.MaxValue ? value : null, "Afterburner"));
                }
                // Reject a poll that changed mid-copy; retry on next tick.
                return view.ReadInt32(20) == polled.ToUnixTimeSeconds() && view.ReadUInt32(0) == 0x4D41484D ? readings.ToArray() : [];
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException or ArgumentException) { }
        }
        return [];
    }

    private static string ReadText(MemoryMappedViewAccessor view, long offset)
    {
        byte[] bytes = new byte[260];
        view.ReadArray(offset, bytes, 0, bytes.Length);
        int end = Array.IndexOf(bytes, (byte)0);
        return Encoding.Latin1.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }
}
