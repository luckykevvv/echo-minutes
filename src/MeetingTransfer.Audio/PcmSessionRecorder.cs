using MeetingTransfer.Core.Audio;
using NAudio.Wave;

namespace MeetingTransfer.Audio;

public sealed class PcmSessionRecorder : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, WaveFileWriter> _writers = [];
    private readonly object _lock = new();
    private bool _disposed;

    public PcmSessionRecorder(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void Write(PcmAudioChunk chunk)
    {
        if (_disposed || chunk.Pcm16.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            var writer = GetWriter(chunk);
            writer.Write(chunk.Pcm16, 0, chunk.Pcm16.Length);
            writer.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }

            _writers.Clear();
            _disposed = true;
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
