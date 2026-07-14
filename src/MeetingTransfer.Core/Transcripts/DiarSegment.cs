namespace MeetingTransfer.Core.Transcripts;

/// <summary>
/// One speaker turn produced by the offline speaker-diarization backend (sherpa-onnx
/// pyannote-segmentation + 3dspeaker embedding cluster). Times are in seconds from
/// the start of the audio file, not chunk-relative. <see cref="SpeakerId"/> is a
/// synthetic integer (0, 1, 2, ...) assigned by the clustering backend; the engine
/// maps it to a stable <c>speaker-N</c> id before constructing TranscriptSegments.
/// </summary>
public sealed record DiarSegment(double StartSec, double EndSec, int SpeakerId);