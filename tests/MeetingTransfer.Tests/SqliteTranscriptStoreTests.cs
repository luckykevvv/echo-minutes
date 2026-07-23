using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Storage;
using MeetingTransfer.Core.Transcripts;
using Microsoft.Data.Sqlite;

namespace MeetingTransfer.Tests;

public sealed class SqliteTranscriptStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "mt-sqlite-tests-" + Guid.NewGuid().ToString("N"));

    public SqliteTranscriptStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveAsync_PersistsAndLoadsCompleteSession()
    {
        var store = CreateStore();
        var document = CreateDocument("Release smoke test", "speaker-1", "Alice", "Hello");

        await store.SaveAsync(document);

        var loaded = await store.LoadAsync(document.SessionId);
        Assert.NotNull(loaded);
        Assert.Equal(document.SessionId, loaded.SessionId);
        Assert.Equal(document.Title, loaded.Title);
        Assert.Equal(document.CreatedAt, loaded.CreatedAt);
        var speaker = Assert.Single(loaded.Speakers);
        Assert.Equal("speaker-1", speaker.Id);
        Assert.Equal("Alice", speaker.Name);
        var segment = Assert.Single(loaded.Segments);
        Assert.Equal("Hello", segment.Text);
        Assert.Equal(TimeSpan.FromSeconds(3), segment.End);
    }

    [Fact]
    public async Task SaveAsync_AllowsSameSpeakerIdInDifferentSessions()
    {
        var store = CreateStore();
        var first = CreateDocument("First", "speaker-1", "Alice", "First text");
        var second = CreateDocument("Second", "speaker-1", "Bob", "Second text");

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        Assert.Equal("Alice", Assert.Single((await store.LoadAsync(first.SessionId))!.Speakers).Name);
        Assert.Equal("Bob", Assert.Single((await store.LoadAsync(second.SessionId))!.Speakers).Name);
        Assert.Equal(2, (await store.ListSessionsAsync()).Count);
    }

    [Fact]
    public async Task SaveAsync_RemovesRowsDeletedOrMergedFromDocument()
    {
        var store = CreateStore();
        var document = CreateDocument("Merge", "speaker-1", "Alice", "One");
        document.EnsureSpeaker("speaker-2", "Bob");
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = "speaker-2",
            SpeakerName = "Bob",
            SourceId = "sample.wav",
            SourceKind = AudioSourceKind.ImportedFile,
            Start = TimeSpan.FromSeconds(3),
            End = TimeSpan.FromSeconds(6),
            Text = "Two"
        });
        await store.SaveAsync(document);

        document.MergeSpeakers("speaker-2", "speaker-1");
        await store.SaveAsync(document);

        var loaded = await store.LoadAsync(document.SessionId);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Speakers);
        Assert.Equal(2, loaded.Segments.Count);
        Assert.All(loaded.Segments, segment => Assert.Equal("speaker-1", segment.SpeakerId));
    }

    [Fact]
    public async Task ListAndDelete_ManageStoredSessions()
    {
        var store = CreateStore();
        var older = CreateDocument("Older", "speaker-1", "Alice", "Old");
        var newer = new TranscriptDocument
        {
            Title = "Newer",
            CreatedAt = older.CreatedAt.AddMinutes(1)
        };
        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var sessions = await store.ListSessionsAsync();
        Assert.Equal(["Newer", "Older"], sessions.Select(session => session.Title));
        Assert.Equal(1, sessions.Single(session => session.Title == "Older").SegmentCount);
        Assert.Equal(TimeSpan.FromSeconds(3), sessions.Single(session => session.Title == "Older").Duration);

        Assert.True(await store.DeleteAsync(older.SessionId));
        Assert.Null(await store.LoadAsync(older.SessionId));
        Assert.False(await store.DeleteAsync(older.SessionId));
        Assert.Equal("Newer", Assert.Single(await store.ListSessionsAsync()).Title);
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyGlobalSpeakerPrimaryKey()
    {
        var databasePath = Path.Combine(_directory, "legacy.sqlite");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await CreateLegacyDatabaseAsync(databasePath, firstId, secondId);

        var store = new SqliteTranscriptStore(databasePath);
        await store.InitializeAsync();

        var first = await store.LoadAsync(firstId);
        var second = await store.LoadAsync(secondId);
        Assert.Equal("Alice", Assert.Single(first!.Speakers).Name);
        Assert.Equal("Bob", Assert.Single(second!.Speakers).Name);
        Assert.Equal("First text", Assert.Single(first.Segments).Text);
        Assert.Equal("Second text", Assert.Single(second.Segments).Text);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task SaveAsync_RejectsSegmentWhoseSpeakerIsMissing()
    {
        var store = CreateStore();
        var document = new TranscriptDocument { Title = "Invalid" };
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = "missing",
            SpeakerName = "Missing",
            Text = "orphan"
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(document));
        Assert.Contains("missing speaker", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.ListSessionsAsync());
    }

    private SqliteTranscriptStore CreateStore()
        => new(Path.Combine(_directory, "meeting.sqlite"));

    private static TranscriptDocument CreateDocument(
        string title,
        string speakerId,
        string speakerName,
        string text)
    {
        var document = new TranscriptDocument { Title = title };
        document.EnsureSpeaker(speakerId, speakerName);
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = speakerId,
            SpeakerName = speakerName,
            SourceId = "sample.wav",
            SourceKind = AudioSourceKind.ImportedFile,
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(3),
            Text = text
        });
        return document;
    }

    private static async Task CreateLegacyDatabaseAsync(
        string databasePath,
        Guid firstId,
        Guid secondId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE sessions (id TEXT PRIMARY KEY, title TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE speakers (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, name TEXT NOT NULL, is_local_user INTEGER NOT NULL);
            CREATE TABLE segments (
                id TEXT PRIMARY KEY, session_id TEXT NOT NULL, speaker_id TEXT NOT NULL,
                speaker_name TEXT NOT NULL, source_id TEXT NOT NULL, source_kind TEXT NOT NULL,
                start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, text TEXT NOT NULL,
                confidence REAL NULL, is_provisional INTEGER NOT NULL);

            INSERT INTO sessions VALUES ($firstId, 'First', $firstCreated);
            INSERT INTO sessions VALUES ($secondId, 'Second', $secondCreated);
            -- This is the broken legacy state: the global speaker-1 row only
            -- survives for the most recently saved session.
            INSERT INTO speakers VALUES ('speaker-1', $secondId, 'Bob', 0);
            INSERT INTO segments VALUES ($firstSegment, $firstId, 'speaker-1', 'Alice', 'first.wav', 'ImportedFile', 0, 1000, 'First text', NULL, 0);
            INSERT INTO segments VALUES ($secondSegment, $secondId, 'speaker-1', 'Bob', 'second.wav', 'ImportedFile', 0, 1000, 'Second text', NULL, 0);
            """;
        command.Parameters.AddWithValue("$firstId", firstId.ToString());
        command.Parameters.AddWithValue("$secondId", secondId.ToString());
        command.Parameters.AddWithValue("$firstCreated", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        command.Parameters.AddWithValue("$secondCreated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$firstSegment", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$secondSegment", Guid.NewGuid().ToString());
        await command.ExecuteNonQueryAsync();
    }
}
