using MeetingTransfer.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTransfer.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly List<WasapiCapture> _captures = [];
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private AudioCaptureRequest? _request;

    public event EventHandler<PcmAudioChunk>? ChunkReady;

    public IReadOnlyList<AudioSource> GetAvailableSources()
    {
        using var enumerator = new MMDeviceEnumerator();
        var sources = new List<AudioSource>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            sources.Add(new AudioSource(device.ID, device.FriendlyName, AudioSourceKind.SystemAudio, false));
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            sources.Add(new AudioSource(device.ID, device.FriendlyName, AudioSourceKind.Microphone, false));
        }

        return sources;
    }

    public Task StartAsync(AudioCaptureRequest request, CancellationToken cancellationToken)
    {
        _request = request;
        using var enumerator = new MMDeviceEnumerator();

        if (request.CaptureSystemAudio)
        {
            var renderDevice = ResolveDevice(enumerator, DataFlow.Render, request.SystemDeviceId);
            var capture = new WasapiLoopbackCapture(renderDevice);
            AttachCapture(capture, renderDevice.ID, AudioSourceKind.SystemAudio);
            capture.StartRecording();
            _captures.Add(capture);
        }

        if (request.CaptureMicrophone)
        {
            var captureDevice = ResolveDevice(enumerator, DataFlow.Capture, request.MicrophoneDeviceId);
            var capture = new WasapiCapture(captureDevice);
            AttachCapture(capture, captureDevice.ID, AudioSourceKind.Microphone);
            capture.StartRecording();
            _captures.Add(capture);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var capture in _captures)
        {
            capture.StopRecording();
            capture.Dispose();
        }

        _captures.Clear();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, DataFlow flow, string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return enumerator.GetDevice(deviceId);
        }

        return flow == DataFlow.Render
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }

    private void AttachCapture(WasapiCapture capture, string sourceId, AudioSourceKind sourceKind)
    {
        capture.DataAvailable += (_, args) =>
        {
            var request = _request;
            if (request is null || args.BytesRecorded == 0)
            {
                return;
            }

            var pcm = WaveFormatConversion.ToPcm16Mono(
                args.Buffer,
                args.BytesRecorded,
                capture.WaveFormat,
                request.TargetSampleRate);

            ChunkReady?.Invoke(this, new PcmAudioChunk(
                sourceId,
                sourceKind,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow - _createdAt,
                request.TargetSampleRate,
                1,
                pcm));
        };
    }
}
