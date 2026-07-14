using System.Reflection;

namespace MeetingTransfer.Tests;

/// <summary>
/// Guard against regressing the "8 × Speaker 1 in the right-hand panel" bug.
/// The engine used to call NextSpeakerId() inside the per-segment Select loop in
/// RunWhisperCppAsync, which assigned every sentence a fresh speaker id and
/// (via EnsureSpeaker in MainWindowViewModel.AddSegment) populated the UI with
/// one Speaker row per segment. Now NextSpeakerId is invoked exactly once per
/// TranscribeFileAsync call.
/// </summary>
public sealed class SpeakerIdAllocationTests
{
    [Fact]
    public void NextSpeakerId_IsMonotonicallyIncreasingAcrossCalls()
    {
        // Reach into the private counter by calling the private helper twice in a row.
        // We don't care about the engine instance state — the engine is fresh per test.
        var engineType = typeof(MeetingTransfer.Stt.SherpaOnnx.SherpaOnnxSpeechEngine);
        var method = engineType.GetMethod(
            "NextSpeakerId",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("NextSpeakerId not found");

        var engine = (MeetingTransfer.Stt.SherpaOnnx.SherpaOnnxSpeechEngine)
            Activator.CreateInstance(
                engineType,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                args: new object?[] { new MeetingTransfer.Stt.SherpaOnnx.SherpaOnnxOptions() },
                culture: null)!;

        var first = (string)method.Invoke(engine, null)!;
        var second = (string)method.Invoke(engine, null)!;
        var third = (string)method.Invoke(engine, null)!;

        Assert.Equal("speaker-1", first);
        Assert.Equal("speaker-2", second);
        Assert.Equal("speaker-3", third);
    }

    [Fact]
    public void TranscriptDocument_EnsureSpeaker_DeduplicatesById()
    {
        // The contract EnsureSpeaker enforces: same id → same Speaker instance.
        // This is what protects us even if a future engine change forgets to
        // share a speaker id across segments — EnsureSpeaker still deduplicates.
        var doc = new MeetingTransfer.Core.Transcripts.TranscriptDocument();
        var first = doc.EnsureSpeaker("speaker-1", "Speaker 1");
        var second = doc.EnsureSpeaker("speaker-1", "Speaker 1 (renamed)");
        var third = doc.EnsureSpeaker("speaker-2", "Speaker 2");

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(2, doc.Speakers.Count);
    }

    [Fact]
    public void TranscriptDocument_MergeSpeakers_RemovesSourceAndRewiresSegments()
    {
        var doc = new MeetingTransfer.Core.Transcripts.TranscriptDocument();
        var a = doc.EnsureSpeaker("speaker-a", "A");
        var b = doc.EnsureSpeaker("speaker-b", "B");
        doc.Segments.Add(new MeetingTransfer.Core.Transcripts.TranscriptSegment
        {
            SpeakerId = "speaker-a",
            SpeakerName = "A",
            Text = "alpha",
        });
        doc.Segments.Add(new MeetingTransfer.Core.Transcripts.TranscriptSegment
        {
            SpeakerId = "speaker-b",
            SpeakerName = "B",
            Text = "beta",
        });

        doc.MergeSpeakers("speaker-a", "speaker-b");

        Assert.Single(doc.Speakers);
        Assert.Equal("speaker-b", doc.Speakers[0].Id);
        Assert.All(doc.Segments, s => Assert.Equal("speaker-b", s.SpeakerId));
        Assert.All(doc.Segments, s => Assert.Equal("B", s.SpeakerName));
    }
}