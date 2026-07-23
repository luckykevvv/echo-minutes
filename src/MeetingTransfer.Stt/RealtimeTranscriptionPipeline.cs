using System.Threading.Channels;
using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;

namespace MeetingTransfer.Stt;

public sealed class RealtimeTranscriptionPipeline
{
    private readonly ISpeechEngine _speechEngine;
    private readonly TranscriptDocument _document;
    private readonly Channel<QueuedChunk> _channel;
    private readonly CancellationToken _processingCancellationToken;
    private readonly Task _worker;
    private int _droppedChunkCount;

    public RealtimeTranscriptionPipeline(
        ISpeechEngine speechEngine,
        TranscriptDocument document,
        int capacity = 32,
        CancellationToken processingCancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _speechEngine = speechEngine;
        _document = document;
        _processingCancellationToken = processingCancellationToken;
        _channel = Channel.CreateBounded<QueuedChunk>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = ConsumeAsync();
    }

    public event EventHandler<TranscriptSegment>? SegmentFinalized;
    public event Action<Exception>? ProcessingFailed;

    public int PendingChunkCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;
    public int DroppedChunkCount => Volatile.Read(ref _droppedChunkCount);

    public bool TryEnqueue(PcmAudioChunk chunk)
    {
        if (_channel.Writer.TryWrite(new QueuedChunk(chunk, null)))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedChunkCount);
        return false;
    }

    // Retained for callers that require acknowledgement of one specific chunk.
    // Realtime capture uses TryEnqueue so the audio callback never accumulates
    // async-void continuations when inference is slower than the input stream.
    public async Task ProcessAsync(PcmAudioChunk chunk, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new QueuedChunk(chunk, completion), cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FinalizeAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        var segments = await _speechEngine.FinalizeSessionAsync(cancellationToken).ConfigureAwait(false);
        PublishSegments(segments);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_processingCancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var segments = await _speechEngine.ProcessAudioAsync(
                        item.Chunk,
                        _processingCancellationToken).ConfigureAwait(false);
                    PublishSegments(segments);
                    item.Completion?.TrySetResult();
                }
                catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
                {
                    item.Completion?.TrySetCanceled(_processingCancellationToken);
                    break;
                }
                catch (Exception ex)
                {
                    item.Completion?.TrySetException(ex);
                    ProcessingFailed?.Invoke(ex);
                }
            }
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            // Recording stopped. Pending chunks are discarded by design so close
            // and stop cannot wait behind an arbitrarily slow recognizer.
        }
        finally
        {
            while (_channel.Reader.TryRead(out var pending))
            {
                pending.Completion?.TrySetCanceled(_processingCancellationToken);
            }
        }
    }

    private void PublishSegments(IReadOnlyList<TranscriptSegment> segments)
    {
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

    private sealed record QueuedChunk(
        PcmAudioChunk Chunk,
        TaskCompletionSource? Completion);
}
