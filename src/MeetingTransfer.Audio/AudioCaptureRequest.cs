namespace MeetingTransfer.Audio;

public sealed record AudioCaptureRequest(
    bool CaptureSystemAudio,
    bool CaptureMicrophone,
    string? SystemDeviceId,
    string? MicrophoneDeviceId,
    int TargetSampleRate,
    int TargetChannels,
    int ChunkMilliseconds,
    TimeSpan TimelineOffset = default);
