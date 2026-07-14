using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Tests;

public sealed class TranscriptDocumentTests
{
    [Fact]
    public void RenameSpeakerUpdatesExistingSegments()
    {
        var document = new TranscriptDocument();
        document.EnsureSpeaker("speaker-1", "Speaker 1");
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = "speaker-1",
            SpeakerName = "Speaker 1",
            Text = "hello"
        });

        document.RenameSpeaker("speaker-1", "Alice");

        Assert.Equal("Alice", document.Speakers.Single().Name);
        Assert.Equal("Alice", document.Segments.Single().SpeakerName);
    }

    [Fact]
    public void MergeSpeakerMovesSegmentsToTargetSpeaker()
    {
        var document = new TranscriptDocument();
        document.EnsureSpeaker("speaker-1", "Alice");
        document.EnsureSpeaker("speaker-2", "Bob");
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = "speaker-2",
            SpeakerName = "Bob",
            Text = "hello"
        });

        document.MergeSpeakers("speaker-2", "speaker-1");

        Assert.Single(document.Speakers);
        Assert.Equal("speaker-1", document.Segments.Single().SpeakerId);
        Assert.Equal("Alice", document.Segments.Single().SpeakerName);
    }
}
