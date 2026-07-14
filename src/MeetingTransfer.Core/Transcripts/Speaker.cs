namespace MeetingTransfer.Core.Transcripts;

public sealed class Speaker
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Speaker";
    public bool IsLocalUser { get; init; }
}
