using MeetingTransfer.Core.Audio;

namespace MeetingTransfer.Audio;

public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<PcmAudioChunk>? ChunkReady;
    IReadOnlyList<AudioSource> GetAvailableSources();
    Task StartAsync(AudioCaptureRequest request, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
