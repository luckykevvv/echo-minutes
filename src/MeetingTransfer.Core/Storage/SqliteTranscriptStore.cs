using System.Data.Common;
using MeetingTransfer.Core.Transcripts;
using Microsoft.Data.Sqlite;

namespace MeetingTransfer.Core.Storage;

public sealed class SqliteTranscriptStore
{
    private readonly string _databasePath;

    public SqliteTranscriptStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath)) ?? ".");
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS speakers (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                name TEXT NOT NULL,
                is_local_user INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS segments (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                speaker_name TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NULL,
                is_provisional INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(TranscriptDocument document, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            INSERT OR REPLACE INTO sessions (id, title, created_at)
            VALUES ($id, $title, $createdAt);
            """, cancellationToken,
            ("$id", document.SessionId.ToString()),
            ("$title", document.Title),
            ("$createdAt", document.CreatedAt.ToString("O"))).ConfigureAwait(false);

        foreach (var speaker in document.Speakers)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT OR REPLACE INTO speakers (id, session_id, name, is_local_user)
                VALUES ($id, $sessionId, $name, $isLocalUser);
                """, cancellationToken,
                ("$id", speaker.Id),
                ("$sessionId", document.SessionId.ToString()),
                ("$name", speaker.Name),
                ("$isLocalUser", speaker.IsLocalUser ? 1 : 0)).ConfigureAwait(false);
        }

        foreach (var segment in document.Segments)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT OR REPLACE INTO segments (
                    id, session_id, speaker_id, speaker_name, source_id, source_kind,
                    start_ms, end_ms, text, confidence, is_provisional)
                VALUES (
                    $id, $sessionId, $speakerId, $speakerName, $sourceId, $sourceKind,
                    $startMs, $endMs, $text, $confidence, $isProvisional);
                """, cancellationToken,
                ("$id", segment.Id.ToString()),
                ("$sessionId", document.SessionId.ToString()),
                ("$speakerId", segment.SpeakerId),
                ("$speakerName", segment.SpeakerName),
                ("$sourceId", segment.SourceId),
                ("$sourceKind", segment.SourceKind.ToString()),
                ("$startMs", (long)segment.Start.TotalMilliseconds),
                ("$endMs", (long)segment.End.TotalMilliseconds),
                ("$text", segment.Text),
                ("$confidence", segment.Confidence),
                ("$isProvisional", segment.IsProvisional ? 1 : 0)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
        => new($"Data Source={_databasePath}");

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
