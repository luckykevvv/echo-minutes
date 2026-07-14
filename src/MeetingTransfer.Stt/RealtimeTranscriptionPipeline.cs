using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Stt;

public sealed class RealtimeTranscriptionPipeline
{
    private readonly ISpeechEngine _speechEngine;
    private readonly TranscriptDocument _document;
    private readonly SemaphoreSlim _processGate = new(1, 1);

    public RealtimeTranscriptionPipeline(ISpeechEngine speechEngine, TranscriptDocument document)
    {
        _speechEngine = speechEngine;
        _document = document;
    }

    public event EventHandler<TranscriptSegment>? SegmentFinalized;

    public async Task ProcessAsync(PcmAudioChunk chunk, CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var segments = await _speechEngine.ProcessAudioAsync(chunk, cancellationToken).ConfigureAwait(false);
            foreach (var segment in segments)
            {
                _document.EnsureSpeaker(
                    segment.SpeakerId,
                    segment.SpeakerName,
                    segment.SourceKind == AudioSourceKind.Microphone);
                _document.Segments.Add(segment);
                SegmentFinalized?.Invoke(this, segment);
            }
        }
        finally
        {
            _processGate.Release();
        }
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var segments = await _speechEngine.FinalizeSessionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var segment in segments)
            {
                _document.EnsureSpeaker(segment.SpeakerId, segment.SpeakerName);
                _document.Segments.Add(segment);
                SegmentFinalized?.Invoke(this, segment);
            }
        }
        finally
        {
            _processGate.Release();
        }
    }
}
