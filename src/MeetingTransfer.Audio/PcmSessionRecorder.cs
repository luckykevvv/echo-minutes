using MeetingTransfer.Core.Audio;
using NAudio.Wave;

namespace MeetingTransfer.Audio;

public sealed class PcmSessionRecorder : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, WaveFileWriter> _writers = [];
    private readonly Dictionary<string, SessionAudioTrack> _tracks = [];
    private readonly object _lock = new();
    private readonly string _recordingId;
    private bool _disposed;

    public PcmSessionRecorder(string directory)
    {
        _directory = directory;
        _recordingId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        Directory.CreateDirectory(directory);
    }

    public void Write(PcmAudioChunk chunk)
    {
        lock (_lock)
        {
            // Stop can race the final capture callback. Re-check under the same
            // lock used by Dispose so a writer cannot be recreated after cleanup.
            if (_disposed || chunk.Pcm16.Length == 0)
            {
                return;
            }

            var writer = GetWriter(chunk);
            writer.Write(chunk.Pcm16, 0, chunk.Pcm16.Length);
            writer.Flush();
        }
    }

    public IReadOnlyList<SessionAudioTrack> RecordedTracks
    {
        get
        {
            lock (_lock)
            {
                return _tracks.Values
                    .Select(track => track with { Duration = TryGetDuration(track.Path) })
                    .ToArray();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }

            _writers.Clear();
        }
    }

    private WaveFileWriter GetWriter(PcmAudioChunk chunk)
    {
        var key = $"{chunk.SourceKind}-{chunk.SourceId}";
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var fileName = $"{_recordingId}-{chunk.SourceKind}-{Sanitize(chunk.SourceId)}.wav";
        var path = Path.Combine(_directory, fileName);
        var writer = new WaveFileWriter(path, new WaveFormat(chunk.SampleRate, 16, chunk.Channels));
        _writers.Add(key, writer);
        _tracks.Add(key, new SessionAudioTrack(
            Guid.NewGuid(),
            path,
            chunk.SourceId,
            chunk.SourceKind,
            chunk.SessionOffset,
            null));
        return writer;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars);
        return sanitized.Length > 80 ? sanitized[..80] : sanitized;
    }

    private static TimeSpan? TryGetDuration(string path)
    {
        try
        {
            using var reader = new WaveFileReader(path);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }
}
