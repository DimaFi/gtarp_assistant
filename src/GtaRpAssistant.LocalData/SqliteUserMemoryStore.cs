using GtaRpAssistant.Core;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.LocalData;

public sealed class SqliteUserMemoryStore : IUserMemoryStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public SqliteUserMemoryStore(string connectionString)
    {
        _connectionString = connectionString;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource)) Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(builder.DataSource))!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS user_memories(
                id TEXT PRIMARY KEY, category INTEGER NOT NULL, content TEXT NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_user_memories_updated ON user_memories(updated_at DESC);
            CREATE TABLE IF NOT EXISTS personality_profile(
                singleton INTEGER PRIMARY KEY CHECK(singleton=1), detail_level INTEGER NOT NULL,
                humor_level INTEGER NOT NULL, initiative_level INTEGER NOT NULL, tone INTEGER NOT NULL,
                adaptive_enabled INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS personality_changes(
                id TEXT PRIMARY KEY, created_at TEXT NOT NULL, trait TEXT NOT NULL,
                old_value INTEGER NOT NULL, new_value INTEGER NOT NULL, reason TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_personality_changes_created ON personality_changes(created_at DESC);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "personality_profile", "adaptive_enabled", "INTEGER NOT NULL DEFAULT 0");
    }

    public IReadOnlyList<UserMemoryItem> List()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,category,content,created_at,updated_at FROM user_memories ORDER BY updated_at DESC;";
            using var reader = command.ExecuteReader();
            var result = new List<UserMemoryItem>();
            while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), (UserMemoryCategory)reader.GetInt32(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4))));
            return result;
        }
    }

    public UserMemoryItem Upsert(Guid? id, UserMemoryCategory category, string content)
    {
        content = content.Trim();
        if (content.Length is < 2 or > 500) throw new ArgumentOutOfRangeException(nameof(content), "Memory must contain 2-500 characters.");
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        lock (_gate)
        {
            using var connection = Open();
            var now = DateTimeOffset.UtcNow;
            var actualId = id ?? Guid.NewGuid();
            var created = now;
            using (var lookup = connection.CreateCommand())
            {
                lookup.CommandText = "SELECT created_at FROM user_memories WHERE id=$id;";
                lookup.Parameters.AddWithValue("$id", actualId.ToString("D"));
                if (lookup.ExecuteScalar() is string value) created = DateTimeOffset.Parse(value);
            }
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_memories(id,category,content,created_at,updated_at) VALUES($id,$category,$content,$created,$updated)
                ON CONFLICT(id) DO UPDATE SET category=excluded.category,content=excluded.content,updated_at=excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$id", actualId.ToString("D"));
            command.Parameters.AddWithValue("$category", (int)category);
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$created", created.ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.ExecuteNonQuery();
            return new(actualId, category, content, created, now);
        }
    }

    public void Delete(Guid id) { lock (_gate) Execute("DELETE FROM user_memories WHERE id=$id;", ("$id", id.ToString("D"))); }
    public void Clear() { lock (_gate) Execute("DELETE FROM user_memories;"); }

    public PersonalityProfile GetPersonality()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT detail_level,humor_level,initiative_level,tone,adaptive_enabled FROM personality_profile WHERE singleton=1;";
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new PersonalityProfile(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4) != 0).Normalize()
                : new PersonalityProfile();
        }
    }

    public void SavePersonality(PersonalityProfile profile)
    {
        profile = profile.Normalize();
        lock (_gate) Execute("""
            INSERT INTO personality_profile(singleton,detail_level,humor_level,initiative_level,tone,adaptive_enabled,updated_at) VALUES(1,$detail,$humor,$initiative,$tone,$adaptive,$updated)
            ON CONFLICT(singleton) DO UPDATE SET detail_level=excluded.detail_level,humor_level=excluded.humor_level,initiative_level=excluded.initiative_level,tone=excluded.tone,adaptive_enabled=excluded.adaptive_enabled,updated_at=excluded.updated_at;
            """, ("$detail", profile.DetailLevel), ("$humor", profile.HumorLevel), ("$initiative", profile.InitiativeLevel), ("$tone", profile.Tone), ("$adaptive", profile.AdaptiveEnabled ? 1 : 0), ("$updated", DateTimeOffset.UtcNow.ToString("O")));
    }

    public IReadOnlyList<PersonalityChange> ListPersonalityChanges(int limit = 50)
    {
        lock (_gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,created_at,trait,old_value,new_value,reason FROM personality_changes ORDER BY created_at DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200)); using var reader = command.ExecuteReader();
            var result = new List<PersonalityChange>();
            while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5)));
            return result;
        }
    }

    public void AddPersonalityChange(PersonalityChange change) { lock (_gate) Execute("INSERT INTO personality_changes(id,created_at,trait,old_value,new_value,reason) VALUES($id,$created,$trait,$old,$new,$reason);", ("$id", change.Id.ToString("D")), ("$created", change.CreatedAt.ToString("O")), ("$trait", change.Trait), ("$old", change.OldValue), ("$new", change.NewValue), ("$reason", change.Reason)); }
    public void ClearPersonalityChanges() { lock (_gate) Execute("DELETE FROM personality_changes;"); }
    public void ResetPersonality() { lock (_gate) { Execute("DELETE FROM personality_profile; DELETE FROM personality_changes;"); } }

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;"; pragma.ExecuteNonQuery(); return connection; }
    private void Execute(string sql, params (string Name, object Value)[] parameters) { using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql; foreach (var p in parameters) command.Parameters.AddWithValue(p.Name, p.Value); command.ExecuteNonQuery(); }
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand(); check.CommandText = $"PRAGMA table_info({table});"; using var reader = check.ExecuteReader();
        while (reader.Read()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close(); using var alter = connection.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};"; alter.ExecuteNonQuery();
    }
}
