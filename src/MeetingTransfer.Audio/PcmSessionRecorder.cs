using MeetingTransfer.Core.Audio;
using NAudio.Wave;

namespace MeetingTransfer.Audio;

public sealed record RecordedAudioTrack(
    string Path,
    string SourceId,
    AudioSourceKind SourceKind);

public sealed class PcmSessionRecorder : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, WaveFileWriter> _writers = [];
    private readonly Dictionary<string, RecordedAudioTrack> _tracks = [];
    private readonly object _lock = new();
    private bool _disposed;

    public PcmSessionRecorder(string directory)
    {
        _directory = directory;
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

    public IReadOnlyList<RecordedAudioTrack> RecordedTracks
    {
        get
        {
            lock (_lock)
            {
                return _tracks.Values.ToArray();
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

        var fileName = $"{chunk.SourceKind}-{Sanitize(chunk.SourceId)}.wav";
        var path = Path.Combine(_directory, fileName);
        var writer = new WaveFileWriter(path, new WaveFormat(chunk.SampleRate, 16, chunk.Channels));
        _writers.Add(key, writer);
        _tracks.Add(key, new RecordedAudioTrack(path, chunk.SourceId, chunk.SourceKind));
        return writer;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars);
        return sanitized.Length > 80 ? sanitized[..80] : sanitized;
    }
}
