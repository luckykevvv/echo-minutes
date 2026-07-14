using MeetingTransfer.Core.Storage;
using MeetingTransfer.Core.Transcripts;
using Microsoft.Data.Sqlite;

namespace MeetingTransfer.Tests;

public sealed class SqliteTranscriptStoreTests
{
    [Fact]
    public async Task SaveAsync_PersistsSessionWithNativeSqliteRuntime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mt-sqlite-tests-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "meeting.sqlite");
        try
        {
            var document = new TranscriptDocument { Title = "Release smoke test" };
            var store = new SqliteTranscriptStore(databasePath);

            await store.SaveAsync(document);

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sessions WHERE id = $id";
            command.Parameters.AddWithValue("$id", document.SessionId.ToString());
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
