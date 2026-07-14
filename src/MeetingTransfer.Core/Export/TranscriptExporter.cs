using System.Text;
using System.Text.Json;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Core.Export;

public static class TranscriptExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Export(TranscriptDocument document, TranscriptExportFormat format)
    {
        return format switch
        {
            TranscriptExportFormat.Text => ExportText(document),
            TranscriptExportFormat.Markdown => ExportMarkdown(document),
            TranscriptExportFormat.Srt => ExportSrt(document),
            TranscriptExportFormat.Vtt => ExportVtt(document),
            TranscriptExportFormat.Json => JsonSerializer.Serialize(document, JsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };
    }

    private static string ExportText(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        foreach (var segment in document.Segments.OrderBy(x => x.Start))
        {
            builder.Append('[')
                .Append(FormatTimestamp(segment.Start))
                .Append("] ")
                .Append(segment.SpeakerName)
                .Append(": ")
                .AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string ExportMarkdown(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title);
        builder.AppendLine();
        foreach (var segment in document.Segments.OrderBy(x => x.Start))
        {
            builder.Append("- **")
                .Append(segment.SpeakerName)
                .Append("** `")
                .Append(FormatTimestamp(segment.Start))
                .Append("` ")
                .AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string ExportSrt(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in document.Segments.OrderBy(x => x.Start))
        {
            builder.AppendLine(index.ToString());
            builder.Append(FormatSrtTimestamp(segment.Start))
                .Append(" --> ")
                .AppendLine(FormatSrtTimestamp(segment.End));
            builder.Append(segment.SpeakerName).Append(": ").AppendLine(segment.Text);
            builder.AppendLine();
            index++;
        }

        return builder.ToString();
    }

    private static string ExportVtt(TranscriptDocument document)
    {
        var builder = new StringBuilder("WEBVTT");
        builder.AppendLine();
        builder.AppendLine();
        foreach (var segment in document.Segments.OrderBy(x => x.Start))
        {
            builder.Append(FormatVttTimestamp(segment.Start))
                .Append(" --> ")
                .AppendLine(FormatVttTimestamp(segment.End));
            builder.Append("<v ")
                .Append(segment.SpeakerName)
                .Append(">")
                .AppendLine(segment.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(TimeSpan value)
        => value.ToString(value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");

    private static string FormatSrtTimestamp(TimeSpan value)
        => value.ToString(@"hh\:mm\:ss\,fff");

    private static string FormatVttTimestamp(TimeSpan value)
        => value.ToString(@"hh\:mm\:ss\.fff");
}
