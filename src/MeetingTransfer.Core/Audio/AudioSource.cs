namespace MeetingTransfer.Core.Audio;

public sealed record AudioSource(
    string Id,
    string DisplayName,
    AudioSourceKind Kind,
    bool IsDefault);
