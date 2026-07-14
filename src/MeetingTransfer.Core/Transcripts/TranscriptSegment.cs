using MeetingTransfer.Core.Audio;

namespace MeetingTransfer.Core.Transcripts;

public sealed class TranscriptSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SpeakerId { get; set; } = "speaker-1";
    public string SpeakerName { get; set; } = "Speaker 1";
    public string SourceId { get; init; } = "unknown";
    public AudioSourceKind SourceKind { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; set; } = "";
    public double? Confidence { get; init; }
    public bool IsProvisional { get; set; }
    public List<WordTiming> Words { get; } = [];
}
