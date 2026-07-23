using System.Data.Common;
using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;
using Microsoft.Data.Sqlite;

namespace MeetingTransfer.Core.Storage;

public sealed record StoredSessionSummary(
    Guid SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    int SpeakerCount,
    int SegmentCount,
    TimeSpan Duration);

public sealed class SqliteTranscriptStore
{
    private const int CurrentSchemaVersion = 2;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public SqliteTranscriptStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath)) ?? ".");
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(connection, transaction, """
                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            if (!await TableExistsAsync(connection, transaction, "speakers", cancellationToken).ConfigureAwait(false))
            {
                await CreateSpeakerTableAsync(connection, transaction, "speakers", cancellationToken).ConfigureAwait(false);
            }
            else if (!await HasSessionScopedPrimaryKeyAsync(connection, transaction, "speakers", cancellationToken).ConfigureAwait(false))
            {
                await MigrateLegacySpeakerTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            var segmentsExist = await TableExistsAsync(connection, transaction, "segments", cancellationToken).ConfigureAwait(false);
            if (segmentsExist)
            {
                // Do this before adding the segment foreign key: a legacy global
                // speaker id may only retain the row for the most recently saved
                // session, while older segments still contain enough data to repair it.
                await ReconstructSpeakersFromSegmentsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            if (!segmentsExist)
            {
                await CreateSegmentTableAsync(connection, transaction, "segments", cancellationToken).ConfigureAwait(false);
            }
            else if (!await HasSessionScopedPrimaryKeyAsync(connection, transaction, "segments", cancellationToken).ConfigureAwait(false))
            {
                await MigrateLegacySegmentTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            // Legacy speaker ids were globally unique and may already have been
            // moved to a later session. Reconstruct any missing session-local rows
            // from the segment snapshots so old transcripts remain loadable.
            await ReconstructSpeakersFromSegmentsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"""
                CREATE INDEX IF NOT EXISTS idx_sessions_created_at
                    ON sessions(created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_segments_session_start
                    ON segments(session_id, start_ms, end_ms);
                PRAGMA user_version = {CurrentSchemaVersion};
                """, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task SaveAsync(TranscriptDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var sessionId = document.SessionId.ToString();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO sessions (id, title, created_at)
            VALUES ($id, $title, $createdAt)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                created_at = excluded.created_at;
            """, cancellationToken,
            ("$id", sessionId),
            ("$title", document.Title),
            ("$createdAt", document.CreatedAt.ToString("O"))).ConfigureAwait(false);

        // A document is the source of truth for one session. Replacing its child
        // rows prevents renamed/merged/deleted speakers and segments from leaving
        // stale records behind in the database.
        await ExecuteAsync(connection, transaction,
            "DELETE FROM segments WHERE session_id = $sessionId; DELETE FROM speakers WHERE session_id = $sessionId;",
            cancellationToken, ("$sessionId", sessionId)).ConfigureAwait(false);

        foreach (var speaker in document.Speakers)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO speakers (session_id, id, name, is_local_user)
                VALUES ($sessionId, $id, $name, $isLocalUser);
                """, cancellationToken,
                ("$sessionId", sessionId),
                ("$id", speaker.Id),
                ("$name", speaker.Name),
                ("$isLocalUser", speaker.IsLocalUser ? 1 : 0)).ConfigureAwait(false);
        }

        foreach (var segment in document.Segments)
        {
            if (!document.Speakers.Any(speaker => string.Equals(speaker.Id, segment.SpeakerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Segment '{segment.Id}' references missing speaker '{segment.SpeakerId}'.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO segments (
                    session_id, id, speaker_id, speaker_name, source_id, source_kind,
                    start_ms, end_ms, text, confidence, is_provisional)
                VALUES (
                    $sessionId, $id, $speakerId, $speakerName, $sourceId, $sourceKind,
                    $startMs, $endMs, $text, $confidence, $isProvisional);
                """, cancellationToken,
                ("$sessionId", sessionId),
                ("$id", segment.Id.ToString()),
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

    public async Task<IReadOnlyList<StoredSessionSummary>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id,
                   s.title,
                   s.created_at,
                   COUNT(DISTINCT p.id) AS speaker_count,
                   COUNT(DISTINCT g.id) AS segment_count,
                   COALESCE(MAX(g.end_ms), 0) AS duration_ms
            FROM sessions s
            LEFT JOIN speakers p ON p.session_id = s.id
            LEFT JOIN segments g ON g.session_id = s.id
            GROUP BY s.id, s.title, s.created_at
            ORDER BY s.created_at DESC;
            """;

        var result = new List<StoredSessionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) ||
                !DateTimeOffset.TryParse(reader.GetString(2), out var createdAt))
            {
                continue;
            }

            result.Add(new StoredSessionSummary(
                id,
                reader.GetString(1),
                createdAt,
                Convert.ToInt32(reader.GetInt64(3)),
                Convert.ToInt32(reader.GetInt64(4)),
                TimeSpan.FromMilliseconds(reader.GetInt64(5))));
        }

        return result;
    }

    public async Task<TranscriptDocument?> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        TranscriptDocument? document = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT title, created_at FROM sessions WHERE id = $id;";
            command.Parameters.AddWithValue("$id", sessionId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) &&
                DateTimeOffset.TryParse(reader.GetString(1), out var createdAt))
            {
                document = new TranscriptDocument
                {
                    SessionId = sessionId,
                    Title = reader.GetString(0),
                    CreatedAt = createdAt
                };
            }
        }

        if (document is null)
        {
            return null;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, name, is_local_user
                FROM speakers
                WHERE session_id = $sessionId
                ORDER BY id;
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                document.Speakers.Add(new Speaker
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    IsLocalUser = reader.GetInt64(2) != 0
                });
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, speaker_id, speaker_name, source_id, source_kind,
                       start_ms, end_ms, text, confidence, is_provisional
                FROM segments
                WHERE session_id = $sessionId
                ORDER BY start_ms, end_ms, id;
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = Enum.TryParse<AudioSourceKind>(reader.GetString(4), ignoreCase: true, out var sourceKind);
                document.Segments.Add(new TranscriptSegment
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    SpeakerId = reader.GetString(1),
                    SpeakerName = reader.GetString(2),
                    SourceId = reader.GetString(3),
                    SourceKind = sourceKind,
                    Start = TimeSpan.FromMilliseconds(reader.GetInt64(5)),
                    End = TimeSpan.FromMilliseconds(reader.GetInt64(6)),
                    Text = reader.GetString(7),
                    Confidence = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    IsProvisional = reader.GetInt64(9) != 0
                });
            }
        }

        return document;
    }

    public async Task<bool> DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private SqliteConnection CreateConnection()
        => new($"Data Source={_databasePath}");

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    private static async Task<bool> HasSessionScopedPrimaryKeyAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        var keys = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys[reader.GetString(1)] = reader.GetInt64(5);
        }

        return keys.GetValueOrDefault("session_id") == 1 && keys.GetValueOrDefault("id") == 2;
    }

    private static Task CreateSpeakerTableAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
        => ExecuteAsync(connection, transaction, $"""
            CREATE TABLE {tableName} (
                session_id TEXT NOT NULL,
                id TEXT NOT NULL,
                name TEXT NOT NULL,
                is_local_user INTEGER NOT NULL,
                PRIMARY KEY (session_id, id),
                FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );
            """, cancellationToken);

    private static Task CreateSegmentTableAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
        => ExecuteAsync(connection, transaction, $"""
            CREATE TABLE {tableName} (
                session_id TEXT NOT NULL,
                id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                speaker_name TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NULL,
                is_provisional INTEGER NOT NULL,
                PRIMARY KEY (session_id, id),
                FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE,
                FOREIGN KEY (session_id, speaker_id) REFERENCES speakers(session_id, id)
            );
            """, cancellationToken);

    private static async Task MigrateLegacySpeakerTableAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await CreateSpeakerTableAsync(connection, transaction, "speakers_v2", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT OR IGNORE INTO speakers_v2 (session_id, id, name, is_local_user)
            SELECT session_id, id, name, is_local_user
            FROM speakers
            WHERE session_id IN (SELECT id FROM sessions);
            DROP TABLE speakers;
            ALTER TABLE speakers_v2 RENAME TO speakers;
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateLegacySegmentTableAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await CreateSegmentTableAsync(connection, transaction, "segments_v2", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT OR IGNORE INTO segments_v2 (
                session_id, id, speaker_id, speaker_name, source_id, source_kind,
                start_ms, end_ms, text, confidence, is_provisional)
            SELECT session_id, id, speaker_id, speaker_name, source_id, source_kind,
                   start_ms, end_ms, text, confidence, is_provisional
            FROM segments
            WHERE session_id IN (SELECT id FROM sessions);
            DROP TABLE segments;
            ALTER TABLE segments_v2 RENAME TO segments;
            """, cancellationToken).ConfigureAwait(false);
    }

    private static Task ReconstructSpeakersFromSegmentsAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
        => ExecuteAsync(connection, transaction, """
            INSERT OR IGNORE INTO speakers (session_id, id, name, is_local_user)
            SELECT session_id,
                   speaker_id,
                   MAX(speaker_name),
                   MAX(CASE WHEN source_kind = 'Microphone' THEN 1 ELSE 0 END)
            FROM segments
            GROUP BY session_id, speaker_id;
            """, cancellationToken);

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
