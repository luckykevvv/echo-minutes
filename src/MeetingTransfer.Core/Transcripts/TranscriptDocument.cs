using MeetingTransfer.Core.Audio;

namespace MeetingTransfer.Core.Transcripts;

public sealed class TranscriptDocument
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled meeting";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<Speaker> Speakers { get; } = [];
    public List<TranscriptSegment> Segments { get; } = [];
    public List<SessionAudioTrack> AudioTracks { get; } = [];

    public Speaker EnsureSpeaker(string speakerId, string defaultName, bool isLocalUser = false)
    {
        var existing = Speakers.FirstOrDefault(x => x.Id == speakerId);
        if (existing is not null)
        {
            return existing;
        }

        var speaker = new Speaker
        {
            Id = speakerId,
            Name = defaultName,
            IsLocalUser = isLocalUser
        };
        Speakers.Add(speaker);
        return speaker;
    }

    public void RenameSpeaker(string speakerId, string name)
    {
        var speaker = Speakers.FirstOrDefault(x => x.Id == speakerId)
            ?? throw new InvalidOperationException($"Speaker '{speakerId}' was not found.");
        speaker.Name = name;

        foreach (var segment in Segments.Where(x => x.SpeakerId == speakerId))
        {
            segment.SpeakerName = name;
        }
    }

    public void MergeSpeakers(string sourceSpeakerId, string targetSpeakerId)
    {
        if (sourceSpeakerId == targetSpeakerId)
        {
            return;
        }

        var target = Speakers.FirstOrDefault(x => x.Id == targetSpeakerId)
            ?? throw new InvalidOperationException($"Target speaker '{targetSpeakerId}' was not found.");

        foreach (var segment in Segments.Where(x => x.SpeakerId == sourceSpeakerId))
        {
            segment.SpeakerId = target.Id;
            segment.SpeakerName = target.Name;
        }

        Speakers.RemoveAll(x => x.Id == sourceSpeakerId);
    }
}
