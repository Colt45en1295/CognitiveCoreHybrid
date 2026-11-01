using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CognitiveCore.App.Services;

public sealed class SqliteMemoryStore
{
    private readonly string _databasePath;

    public SqliteMemoryStore(string databasePath)
    {
        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS episodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                prompt TEXT NOT NULL,
                response TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
        ";
        command.ExecuteNonQuery();
    }

    public async Task AddAsync(string prompt, string response, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO episodes (prompt, response) VALUES (@prompt, @response);";
        command.Parameters.AddWithValue("@prompt", prompt);
        command.Parameters.AddWithValue("@response", response);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT prompt, response FROM episodes ORDER BY id DESC LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var prompt = reader.GetString(0);
            var response = reader.GetString(1);
            items.Add($"Prompt: {prompt}\nResponse: {response}");
        }

        return items;
    }
}

public sealed class AppendOnlyEventLedger
{
    private readonly string _databasePath;

    public AppendOnlyEventLedger(string databasePath)
    {
        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
        ";
        command.ExecuteNonQuery();

        using var triggerCommand = connection.CreateCommand();
        triggerCommand.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS stop_event_updates
            BEFORE UPDATE ON events
            BEGIN
                SELECT RAISE(ABORT, 'events are append-only');
            END;
        ";
        triggerCommand.ExecuteNonQuery();

        using var deleteTriggerCommand = connection.CreateCommand();
        deleteTriggerCommand.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS stop_event_deletes
            BEFORE DELETE ON events
            BEGIN
                SELECT RAISE(ABORT, 'events are append-only');
            END;
        ";
        deleteTriggerCommand.ExecuteNonQuery();
    }

    public async Task AppendAsync(string eventType, object payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serialized = JsonSerializer.Serialize(payload);
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events (event_type, payload) VALUES (@eventType, @payload);";
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@payload", serialized);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
