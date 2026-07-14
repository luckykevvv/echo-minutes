namespace MeetingTransfer.Core.Audio;

public sealed record PcmAudioChunk(
    string SourceId,
    AudioSourceKind SourceKind,
    DateTimeOffset CapturedAt,
    TimeSpan SessionOffset,
    int SampleRate,
    int Channels,
    byte[] Pcm16);
