using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CognitiveCore.App.Services;

public sealed class EventLedger
{
    private readonly string _databasePath;

    public EventLedger(string databasePath)
    {
        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(databasePath) ?? ".";
        Directory.CreateDirectory(directory);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                payload TEXT NOT NULL,
                previous_hash TEXT,
                chain_hash TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT(datetime('now'))
            );
        ";
        command.ExecuteNonQuery();

        using var trigger = connection.CreateCommand();
        trigger.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS stop_event_updates
            BEFORE UPDATE ON events
            BEGIN
                SELECT RAISE(ABORT, 'events are append-only');
            END;
        ";
        trigger.ExecuteNonQuery();

        using var deleteTrigger = connection.CreateCommand();
        deleteTrigger.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS stop_event_deletes
            BEFORE DELETE ON events
            BEGIN
                SELECT RAISE(ABORT, 'events are append-only');
            END;
        ";
        deleteTrigger.ExecuteNonQuery();
    }

    public async Task AppendAsync(string eventType, object payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serialized = JsonSerializer.Serialize(payload);
        var previousHash = await GetLatestChainHashAsync(cancellationToken);
        var currentHash = ComputeChainHash(previousHash, eventType, serialized, DateTimeOffset.UtcNow);

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO events (event_type, payload, previous_hash, chain_hash)
            VALUES (@eventType, @payload, @previousHash, @currentHash);
        ";
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@payload", serialized);
        command.Parameters.AddWithValue("@previousHash", previousHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@currentHash", currentHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetLatestChainHashAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT chain_hash FROM events ORDER BY id DESC LIMIT 1;";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? null : result.ToString();
    }

    public static string ComputeChainHash(string? previousHash, string eventType, string payloadJson, DateTimeOffset timestamp)
    {
        var seed = string.Concat(
            previousHash ?? string.Empty,
            "|",
            eventType,
            "|",
            payloadJson,
            "|",
            timestamp.ToString("O"));

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(seed);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
