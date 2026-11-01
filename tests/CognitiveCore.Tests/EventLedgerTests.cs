using System;
using System.IO;
using CognitiveCore.App.Services;
using Xunit;

namespace CognitiveCore.Tests;

public class EventLedgerTests
{
    [Fact]
    public async Task AppendAsync_WritesEventsToSqlite()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"cognitivecore-events-{Guid.NewGuid():N}.db");
        try
        {
            var ledger = new AppendOnlyEventLedger(tempFile);
            await ledger.AppendAsync("unit.test", new { message = "hello" });

            var connectionString = $"Data Source={tempFile}";
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM events WHERE event_type = 'unit.test';";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.Equal(1, count);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
