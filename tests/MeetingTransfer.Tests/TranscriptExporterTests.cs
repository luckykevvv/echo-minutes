using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Export;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Tests;

public sealed class TranscriptExporterTests
{
    [Fact]
    public void ExportsMarkdownWithSpeakerAndTimestamp()
    {
        var document = new TranscriptDocument { Title = "Demo" };
        document.EnsureSpeaker("speaker-1", "Alice");
        document.Segments.Add(new TranscriptSegment
        {
            SpeakerId = "speaker-1",
            SpeakerName = "Alice",
            SourceKind = AudioSourceKind.ImportedFile,
            Start = TimeSpan.FromSeconds(12),
            End = TimeSpan.FromSeconds(16),
            Text = "hello world"
        });

        var markdown = TranscriptExporter.Export(document, TranscriptExportFormat.Markdown);

        Assert.Contains("# Demo", markdown);
        Assert.Contains("Alice", markdown);
        Assert.Contains("hello world", markdown);
    }
}
