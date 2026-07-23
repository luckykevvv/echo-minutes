using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Core.Audio;

public sealed record SessionAudioTrack(
    Guid Id,
    string Path,
    string SourceId,
    AudioSourceKind SourceKind,
    TimeSpan TimelineOffset,
    TimeSpan? Duration);

public static class SessionAudioTrackResolver
{
    public static SessionAudioTrack? Resolve(
        IReadOnlyList<SessionAudioTrack> tracks,
        TranscriptSegment segment)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(segment);

        return tracks
            .Where(track =>
                track.SourceKind == segment.SourceKind &&
                string.Equals(track.SourceId, segment.SourceId, StringComparison.Ordinal) &&
                track.TimelineOffset <= segment.Start)
            .OrderByDescending(track => track.TimelineOffset)
            .FirstOrDefault();
    }

    public static TimeSpan GetSeekPosition(
        SessionAudioTrack track,
        TranscriptSegment segment)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(segment);

        var position = segment.Start - track.TimelineOffset;
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (track.Duration is { } duration && duration > TimeSpan.Zero && position > duration)
        {
            return duration;
        }

        return position;
    }
}
