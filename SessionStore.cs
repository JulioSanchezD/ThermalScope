using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace ThermalScope;

public sealed class SessionStore : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SessionStore(string directory)
    {
        Directory.CreateDirectory(directory);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "sessions.sqlite"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, metadata TEXT NOT NULL); CREATE TABLE IF NOT EXISTS frames(session_id TEXT NOT NULL, sequence INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(session_id,sequence)); CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);");
        // Preserve any samples from a crashed/force-closed run and mark the run interrupted.
        foreach (var session in List().Where(s => s.Status == "recording"))
        {
            var frames = ReadFrames(session.Id);
            Save(session with { Status = "interrupted", Ended = frames.LastOrDefault()?.Time ?? session.Started });
        }
    }

    private void Execute(string sql)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    public Dictionary<string, string> GetMappings()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT value FROM settings WHERE key='mappings'";
            return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? new() : new();
        }
    }

    public void SetMappings(Dictionary<string, string> mappings)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT OR REPLACE INTO settings VALUES('mappings',$value)";
            cmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(mappings, Json)); cmd.ExecuteNonQuery();
        }
    }

    public void Save(Session session)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT OR REPLACE INTO sessions VALUES($id,$metadata)";
            cmd.Parameters.AddWithValue("$id", session.Id); cmd.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(session, Json)); cmd.ExecuteNonQuery();
        }
    }

    public void Append(string id, Frame frame)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO frames VALUES($id,$seq,$data)";
            cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$seq", frame.Sequence);
            cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(frame, Json)); cmd.ExecuteNonQuery();
        }
    }

    public Session[] List()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT metadata,(SELECT COUNT(*) FROM frames f WHERE f.session_id=s.id) FROM sessions s ORDER BY rowid DESC";
            using var reader = cmd.ExecuteReader(); var result = new List<Session>();
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<Session>(reader.GetString(0), Json)! with { Samples = reader.GetInt32(1) });
            return result.ToArray();
        }
    }

    public Frame[] ReadFrames(string id)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM frames WHERE session_id=$id ORDER BY sequence";
            cmd.Parameters.AddWithValue("$id", id); using var reader = cmd.ExecuteReader(); var result = new List<Frame>();
            // Keep full raw readings in SQLite, but send compact chart frames to the phone.
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<Frame>(reader.GetString(0), Json)! with { Sensors = [] });
            return result.ToArray();
        }
    }

    public void Dispose() { lock (gate) db.Dispose(); }
}
