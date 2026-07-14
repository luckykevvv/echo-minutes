using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.Tests;

public sealed class SpeakerDiarizationTests
{
    [Fact]
    public void Options_DefaultToAutomaticSpeakerCountWithSaferThreshold()
    {
        var options = new SherpaOnnxOptions();

        Assert.Equal(-1, options.DiarizationClusterCount);
        Assert.Equal(0.9, options.DiarizationClusterThreshold);
    }

    [Theory]
    [InlineData("progress 0.19%", 0.19)]
    [InlineData("progress 73.87%", 73.87)]
    [InlineData("progress 100.00%", 100.0)]
    public void TryParseDiarizationProgress_ParsesRealCliOutput(string line, double expected)
    {
        Assert.True(SherpaOnnxSpeechEngine.TryParseDiarizationProgress(line, out var actual));
        Assert.Equal(expected, actual, 2);
    }

    [Fact]
    public void ParseDiarizationOutput_ExtractsSpeakerTurnsAndIgnoresDiagnostics()
    {
        const string output = """
            OfflineSpeakerDiarizationConfig(...)
            Started
            0.638 -- 6.848 speaker_00
            7.017 -- 10.679 speaker_01
            Duration : 56.861 s
            """;

        var turns = SherpaOnnxSpeechEngine.ParseDiarizationOutput(output);

        Assert.Equal(2, turns.Count);
        Assert.Equal(new DiarSegment(0.638, 6.848, 0), turns[0]);
        Assert.Equal(new DiarSegment(7.017, 10.679, 1), turns[1]);
    }

    [Fact]
    public void AssignSpeakers_UsesGreatestTimeOverlap()
    {
        var segments = new List<TranscriptSegment>
        {
            Segment(1, 5),
            Segment(7.5, 10),
            Segment(10.8, 12),
        };
        var turns = new List<DiarSegment>
        {
            new(0.5, 6, 0),
            new(7, 10.5, 1),
            new(11, 13, 0),
        };

        SherpaOnnxSpeechEngine.AssignSpeakers(segments, turns);

        Assert.Equal(["speaker-1", "speaker-2", "speaker-1"], segments.Select(x => x.SpeakerId));
        Assert.Equal(["Speaker 1", "Speaker 2", "Speaker 1"], segments.Select(x => x.SpeakerName));
    }

    [Fact]
    public void SplitAndAssignSpeakers_SplitsTranscriptThatSpansMultipleTurns()
    {
        var transcript = new List<TranscriptSegment>
        {
            new()
            {
                SourceId = "meeting.wav",
                SourceKind = AudioSourceKind.ImportedFile,
                Start = TimeSpan.Zero,
                End = TimeSpan.FromSeconds(12),
                Text = "AAAABBBBCCCC",
            },
        };
        var turns = new List<DiarSegment>
        {
            new(0, 4, 0),
            new(4, 8, 1),
            new(8, 12, 2),
        };

        var result = SherpaOnnxSpeechEngine.SplitAndAssignSpeakers(transcript, turns);

        Assert.Equal(3, result.Count);
        Assert.Equal(["speaker-1", "speaker-2", "speaker-3"], result.Select(x => x.SpeakerId));
        Assert.Equal(["AAAA", "BBBB", "CCCC"], result.Select(x => x.Text));
        Assert.Equal("meeting.wav", result[1].SourceId);
    }

    [Fact]
    public void SplitAndAssignSpeakers_RenumbersSparseClusterIdsByFirstAppearance()
    {
        var transcript = new List<TranscriptSegment>
        {
            Segment(0, 3),
            Segment(3, 6),
            Segment(6, 9),
        };
        var turns = new List<DiarSegment>
        {
            new(0, 3, 4),
            new(3, 6, 12),
            new(6, 9, 4),
        };

        var result = SherpaOnnxSpeechEngine.SplitAndAssignSpeakers(transcript, turns);

        Assert.Equal(["speaker-1", "speaker-2", "speaker-1"], result.Select(x => x.SpeakerId));
        Assert.Equal(["Speaker 1", "Speaker 2", "Speaker 1"], result.Select(x => x.SpeakerName));
    }

    private static TranscriptSegment Segment(double start, double end) => new()
    {
        SourceKind = AudioSourceKind.ImportedFile,
        Start = TimeSpan.FromSeconds(start),
        End = TimeSpan.FromSeconds(end),
        Text = "test",
    };
}
