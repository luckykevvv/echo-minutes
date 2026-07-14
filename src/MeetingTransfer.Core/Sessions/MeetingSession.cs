using MeetingTransfer.Core.Audio;

namespace MeetingTransfer.Core.Sessions;

public sealed class MeetingSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled meeting";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public List<AudioSource> Sources { get; } = [];
}
