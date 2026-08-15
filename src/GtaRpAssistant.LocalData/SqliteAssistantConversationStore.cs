using System.Globalization;
using System.Text.Json;
using GtaRpAssistant.Core;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.LocalData;

public sealed class SqliteAssistantConversationStore : IAssistantConversationStore
{
    private const string DefaultTitle = "Новый диалог";
    private const int CacheCapacity = 200;
    private readonly object _gate = new();
    private readonly string _connectionString;
    private Guid _currentConversationId;
    private List<AssistantConversationTurn> _currentTurns = [];

    public SqliteAssistantConversationStore(string connectionString)
    {
        _connectionString = connectionString;
        EnsureDirectory();
        try
        {
            InitializeSchema();
        }
        catch (SqliteException)
        {
            RecoverDatabase();
            InitializeSchema();
        }
        LoadCurrentConversation();
    }

    public Guid CurrentConversationId { get { lock (_gate) return _currentConversationId; } }

    public void Add(AssistantConversationTurn turn)
    {
        lock (_gate)
        {
            var stored = Sanitize(turn);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var title = stored.Role == ConversationRole.User ? ConversationTitleGenerator.FromContext(stored.Text) : DefaultTitle;
            EnsureConversation(connection, transaction, _currentConversationId, title, stored.CreatedAt);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR REPLACE INTO messages(
                        id, conversation_id, created_at, role, text, provider_id, model_id, used_fact_ids_json, situation_id)
                    VALUES($id,$conversation,$created,$role,$text,$provider,$model,$facts,$situation);
                    """;
                command.Parameters.AddWithValue("$id", stored.Id.ToString("D"));
                command.Parameters.AddWithValue("$conversation", _currentConversationId.ToString("D"));
                command.Parameters.AddWithValue("$created", stored.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$role", (int)stored.Role);
                command.Parameters.AddWithValue("$text", stored.Text);
                command.Parameters.AddWithValue("$provider", (object?)stored.ProviderId ?? DBNull.Value);
                command.Parameters.AddWithValue("$model", (object?)stored.ModelId ?? DBNull.Value);
                command.Parameters.AddWithValue("$facts", JsonSerializer.Serialize(stored.UsedFactIds));
                command.Parameters.AddWithValue("$situation", (object?)stored.SituationId ?? DBNull.Value);
                command.ExecuteNonQuery();
            }

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE conversations
                    SET updated_at=$updated,
                        title=CASE WHEN title=$default AND $role=$userRole THEN $title ELSE title END
                    WHERE id=$id;
                    """;
                update.Parameters.AddWithValue("$updated", stored.CreatedAt.ToString("O"));
                update.Parameters.AddWithValue("$default", DefaultTitle);
                update.Parameters.AddWithValue("$role", (int)stored.Role);
                update.Parameters.AddWithValue("$userRole", (int)ConversationRole.User);
                update.Parameters.AddWithValue("$title", title);
                update.Parameters.AddWithValue("$id", _currentConversationId.ToString("D"));
                update.ExecuteNonQuery();
            }
            transaction.Commit();

            _currentTurns.Add(stored);
            if (_currentTurns.Count > CacheCapacity) _currentTurns.RemoveRange(0, _currentTurns.Count - CacheCapacity);
        }
    }

    public AssistantConversationSnapshot GetCurrent()
    {
        lock (_gate) return Snapshot(_currentTurns);
    }

    public AssistantConversationSnapshot GetRelevant(ConversationContextQuery query)
    {
        lock (_gate)
        {
            var cutoff = DateTimeOffset.UtcNow - (query.MaxAge ?? TimeSpan.FromMinutes(12));
            var turns = _currentTurns.Where(x => x.CreatedAt >= cutoff && (string.IsNullOrWhiteSpace(query.SituationId)
                || string.IsNullOrWhiteSpace(x.SituationId)
                || string.Equals(x.SituationId, query.SituationId, StringComparison.Ordinal)))
                .TakeLast(Math.Clamp(query.MaxTurns, 1, 12)).ToArray();
            return Snapshot(turns);
        }
    }

    public IReadOnlyList<AssistantConversationInfo> ListConversations(int limit = 50)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.id,c.title,c.created_at,c.updated_at,COUNT(m.id)
                FROM conversations c LEFT JOIN messages m ON m.conversation_id=c.id
                GROUP BY c.id,c.title,c.created_at,c.updated_at
                ORDER BY c.updated_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
            using var reader = command.ExecuteReader();
            var result = new List<AssistantConversationInfo>();
            while (reader.Read())
                result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), ParseDate(reader.GetString(2)), ParseDate(reader.GetString(3)), reader.GetInt32(4)));
            return result;
        }
    }

    public bool TryOpenConversation(Guid conversationId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            if (!ConversationExists(connection, conversationId)) return false;
            _currentConversationId = conversationId;
            SaveCurrentConversation(connection, null, conversationId);
            _currentTurns = LoadTurns(connection, conversationId);
            return true;
        }
    }

    public void RenameConversation(Guid conversationId, string title)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE conversations SET title=$title,updated_at=$updated WHERE id=$id;";
            command.Parameters.AddWithValue("$title", NormalizeTitle(title));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", conversationId.ToString("D"));
            command.ExecuteNonQuery();
        }
    }

    public void DeleteConversation(Guid conversationId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM conversations WHERE id=$id;";
                command.Parameters.AddWithValue("$id", conversationId.ToString("D"));
                command.ExecuteNonQuery();
            }

            if (_currentConversationId == conversationId)
            {
                _currentConversationId = MostRecentConversation(connection, transaction) ?? Guid.NewGuid();
                EnsureConversation(connection, transaction, _currentConversationId, DefaultTitle, DateTimeOffset.UtcNow);
                SaveCurrentConversation(connection, transaction, _currentConversationId);
            }
            transaction.Commit();
            if (_currentConversationId == conversationId) throw new InvalidOperationException("Current conversation was not replaced.");
            _currentTurns = LoadTurns(connection, _currentConversationId);
        }
    }

    public void StartNewConversation()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            EnsureConversation(connection, transaction, id, DefaultTitle, now);
            SaveCurrentConversation(connection, transaction, id);
            transaction.Commit();
            _currentConversationId = id;
            _currentTurns = [];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM messages WHERE conversation_id=$id;";
                command.Parameters.AddWithValue("$id", _currentConversationId.ToString("D"));
                command.ExecuteNonQuery();
            }
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE conversations SET title=$title,updated_at=$updated WHERE id=$id;";
                update.Parameters.AddWithValue("$title", DefaultTitle);
                update.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                update.Parameters.AddWithValue("$id", _currentConversationId.ToString("D"));
                update.ExecuteNonQuery();
            }
            transaction.Commit();
            _currentTurns.Clear();
        }
    }

    private void LoadCurrentConversation()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            var stored = ReadCurrentConversation(connection);
            _currentConversationId = stored is not null && ConversationExists(connection, stored.Value) ? stored.Value : Guid.NewGuid();
            using var transaction = connection.BeginTransaction();
            EnsureConversation(connection, transaction, _currentConversationId, DefaultTitle, DateTimeOffset.UtcNow);
            SaveCurrentConversation(connection, transaction, _currentConversationId);
            transaction.Commit();
            _currentTurns = LoadTurns(connection, _currentConversationId);
        }
    }

    private void InitializeSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS conversations(
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS messages(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                created_at TEXT NOT NULL,
                role INTEGER NOT NULL,
                text TEXT NOT NULL,
                provider_id TEXT NULL,
                model_id TEXT NULL,
                used_fact_ids_json TEXT NOT NULL DEFAULT '[]',
                situation_id TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS app_state(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_messages_conversation_created ON messages(conversation_id,created_at,id);
            CREATE INDEX IF NOT EXISTS idx_conversations_updated ON conversations(updated_at DESC);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void EnsureDirectory()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        if (builder.DataSource is "" or ":memory:") return;
        var fullPath = Path.GetFullPath(builder.DataSource);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    }

    private void RecoverDatabase()
    {
        SqliteConnection.ClearAllPools();
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        if (builder.DataSource is "" or ":memory:") throw new InvalidDataException("The in-memory assistant database is corrupt.");
        var fullPath = Path.GetFullPath(builder.DataSource);
        var suffix = $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}";
        foreach (var source in new[] { fullPath, fullPath + "-wal", fullPath + "-shm" })
            if (File.Exists(source)) File.Move(source, source + suffix, false);
    }

    private static void EnsureConversation(SqliteConnection connection, SqliteTransaction? transaction, Guid id, string title, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO conversations(id,title,created_at,updated_at) VALUES($id,$title,$created,$updated);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void SaveCurrentConversation(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO app_state(key,value) VALUES('current_conversation_id',$id);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static Guid? ReadCurrentConversation(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key='current_conversation_id';";
        return command.ExecuteScalar() is string value && Guid.TryParse(value, out var id) ? id : null;
    }

    private static bool ConversationExists(SqliteConnection connection, Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static Guid? MostRecentConversation(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM conversations ORDER BY updated_at DESC LIMIT 1;";
        return command.ExecuteScalar() is string value && Guid.TryParse(value, out var id) ? id : null;
    }

    private static List<AssistantConversationTurn> LoadTurns(SqliteConnection connection, Guid conversationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,created_at,role,text,provider_id,model_id,used_fact_ids_json,situation_id
            FROM messages WHERE conversation_id=$conversation
            ORDER BY created_at DESC,id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$conversation", conversationId.ToString("D"));
        command.Parameters.AddWithValue("$limit", CacheCapacity);
        using var reader = command.ExecuteReader();
        var result = new List<AssistantConversationTurn>();
        while (reader.Read())
        {
            var facts = ParseFactIds(reader.GetString(6));
            result.Add(new(
                Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                ParseDate(reader.GetString(1)),
                Enum.IsDefined(typeof(ConversationRole), reader.GetInt32(2)) ? (ConversationRole)reader.GetInt32(2) : ConversationRole.User,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                facts,
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        result.Reverse();
        return result;
    }

    private static IReadOnlyList<string> ParseFactIds(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json)?.Where(x => !string.IsNullOrWhiteSpace(x)).Take(12).ToArray() ?? []; }
        catch (JsonException) { return []; }
    }

    private static AssistantConversationTurn Sanitize(AssistantConversationTurn turn) => turn with
    {
        Text = Limit(turn.Text.Trim(), 1200),
        UsedFactIds = turn.UsedFactIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(12).ToArray(),
    };

    private static AssistantConversationSnapshot Snapshot(IEnumerable<AssistantConversationTurn> turns)
    {
        var copy = turns.ToArray();
        return new(copy, copy.LastOrDefault()?.CreatedAt, copy.LastOrDefault()?.SituationId);
    }

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string NormalizeTitle(string title)
    {
        var normalized = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length == 0) return DefaultTitle;
        return normalized.Length <= 80 ? normalized : normalized[..79] + "…";
    }
    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
}
