using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Stt;

/// <summary>
/// Lightweight progress report emitted while a long-running transcription
/// (imported file, future online model) is in flight. We deliberately keep this
/// value-object simple so the UI can decide how to render it (ProgressBar label,
/// log line, toast, etc).
/// </summary>
public readonly record struct TranscriptionProgress(
    TranscriptionStage Stage,
    double Percent,
    string? Message)
{
    public static TranscriptionProgress ForStage(TranscriptionStage stage, double percent, string? message = null)
        => new(stage, Math.Clamp(percent, 0d, 100d), message);

    public static TranscriptionProgress Indeterminate(TranscriptionStage stage, string? message = null)
        => new(stage, 0d, message);
}

public enum TranscriptionStage
{
    Preparing,
    LoadingModel,
    Transcribing,
    PostProcessing,
    AsrComplete,
    Diarizing,
    Finalizing,
    Complete,
}

public interface ISpeechEngine : IAsyncDisposable
{
    string Name { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TranscriptSegment>> ProcessAudioAsync(
        PcmAudioChunk chunk,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(
        string wavPath,
        string sourceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Long-running offline transcription with progress callbacks. The default
    /// implementation may simply ignore the progress callback and return the
    /// same payload as the parameterless overload.
    /// </summary>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(
        string wavPath,
        string sourceId,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranscriptSegment>> FinalizeSessionAsync(CancellationToken cancellationToken);
}
