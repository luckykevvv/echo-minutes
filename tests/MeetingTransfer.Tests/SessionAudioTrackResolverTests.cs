using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Tests;

public sealed class SessionAudioTrackResolverTests
{
    [Fact]
    public void Resolve_SelectsLatestMatchingTrackBeforeSegment()
    {
        var first = CreateTrack("mic", AudioSourceKind.Microphone, 0, 20);
        var second = CreateTrack("mic", AudioSourceKind.Microphone, 20, 15);
        var wrongSource = CreateTrack("speaker", AudioSourceKind.SystemAudio, 20, 15);
        var segment = new TranscriptSegment
        {
            SourceId = "mic",
            SourceKind = AudioSourceKind.Microphone,
            Start = TimeSpan.FromSeconds(24),
            End = TimeSpan.FromSeconds(27),
            Text = "continued recording"
        };

        var resolved = SessionAudioTrackResolver.Resolve([first, wrongSource, second], segment);

        Assert.Same(second, resolved);
        Assert.Equal(TimeSpan.FromSeconds(4), SessionAudioTrackResolver.GetSeekPosition(resolved!, segment));
    }

    [Fact]
    public void Resolve_DoesNotGuessAcrossDifferentSources()
    {
        var segment = new TranscriptSegment
        {
            SourceId = "missing-mic",
            SourceKind = AudioSourceKind.Microphone,
            Start = TimeSpan.FromSeconds(2),
            End = TimeSpan.FromSeconds(3),
            Text = "no matching track"
        };

        Assert.Null(SessionAudioTrackResolver.Resolve(
            [CreateTrack("another-mic", AudioSourceKind.Microphone, 0, 10)],
            segment));
    }

    [Fact]
    public void GetSeekPosition_ClampsToKnownTrackDuration()
    {
        var track = CreateTrack("mic", AudioSourceKind.Microphone, 10, 5);
        var segment = new TranscriptSegment
        {
            SourceId = "mic",
            SourceKind = AudioSourceKind.Microphone,
            Start = TimeSpan.FromSeconds(18),
            End = TimeSpan.FromSeconds(20),
            Text = "late segment"
        };

        Assert.Equal(TimeSpan.FromSeconds(5), SessionAudioTrackResolver.GetSeekPosition(track, segment));
    }

    private static SessionAudioTrack CreateTrack(
        string sourceId,
        AudioSourceKind sourceKind,
        double offsetSeconds,
        double durationSeconds)
        => new(
            Guid.NewGuid(),
            $"{sourceId}-{offsetSeconds}.wav",
            sourceId,
            sourceKind,
            TimeSpan.FromSeconds(offsetSeconds),
            TimeSpan.FromSeconds(durationSeconds));
}
