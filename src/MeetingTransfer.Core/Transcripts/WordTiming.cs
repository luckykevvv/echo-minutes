namespace MeetingTransfer.Core.Transcripts;

public sealed record WordTiming(
    string Text,
    TimeSpan Start,
    TimeSpan End,
    double? Confidence);
