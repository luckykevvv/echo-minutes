using NAudio.Wave;

namespace MeetingTransfer.Audio;

public sealed class AudioPlaybackStoppedEventArgs(Exception? exception) : EventArgs
{
    public Exception? Exception { get; } = exception;
}

public interface IAudioPlaybackService : IDisposable
{
    event EventHandler<AudioPlaybackStoppedEventArgs>? PlaybackStopped;

    bool IsPlaying { get; }
    string? CurrentPath { get; }

    void Play(string path, TimeSpan position);
    void Stop();
}

public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public event EventHandler<AudioPlaybackStoppedEventArgs>? PlaybackStopped;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _output?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public string? CurrentPath { get; private set; }

    public void Play(string path, TimeSpan position)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The session recording could not be found.", path);
        }

        lock (_gate)
        {
            DisposeCurrent();
            var reader = new AudioFileReader(path);
            var output = new WaveOutEvent();
            try
            {
                var clamped = position < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : position > reader.TotalTime
                        ? reader.TotalTime
                        : position;
                reader.CurrentTime = clamped;
                output.PlaybackStopped += Output_PlaybackStopped;
                output.Init(reader);
                _reader = reader;
                _output = output;
                CurrentPath = path;
                output.Play();
            }
            catch
            {
                _output = null;
                _reader = null;
                CurrentPath = null;
                output.PlaybackStopped -= Output_PlaybackStopped;
                output.Dispose();
                reader.Dispose();
                throw;
            }
        }
    }

    public void Stop()
    {
        WaveOutEvent? output;
        lock (_gate)
        {
            output = _output;
        }

        try { output?.Stop(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeCurrent();
        }
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            DisposeCurrent();
        }

        PlaybackStopped?.Invoke(this, new AudioPlaybackStoppedEventArgs(e.Exception));
    }

    private void DisposeCurrent()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        CurrentPath = null;

        if (output is not null)
        {
            output.PlaybackStopped -= Output_PlaybackStopped;
            try { output.Stop(); } catch { }
            output.Dispose();
        }

        reader?.Dispose();
    }
}
