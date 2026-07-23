using MeetingTransfer.Core.Audio;
using MeetingTransfer.Core.Transcripts;
using MeetingTransfer.Stt;

namespace MeetingTransfer.Tests;

public sealed class RealtimeTranscriptionPipelineTests
{
    [Fact]
    public async Task FinalizeAsync_WaitsForInFlightChunk()
    {
        var engine = new BlockingSpeechEngine();
        var pipeline = new RealtimeTranscriptionPipeline(engine, new TranscriptDocument());
        var chunk = new PcmAudioChunk(
            "mic",
            AudioSourceKind.Microphone,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            16000,
            1,
            [0, 0]);

        var processing = pipeline.ProcessAsync(chunk, CancellationToken.None);
        await engine.ProcessEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var finalizing = pipeline.FinalizeAsync(CancellationToken.None);

        Assert.False(finalizing.IsCompleted);
        engine.AllowProcessToFinish.TrySetResult();
        await Task.WhenAll(processing, finalizing);
        Assert.Equal(["process-start", "process-end", "finalize"], engine.Events);
    }

    [Fact]
    public async Task TryEnqueue_UsesBoundedQueueAndCountsRejectedChunks()
    {
        var engine = new BlockingSpeechEngine();
        var pipeline = new RealtimeTranscriptionPipeline(
            engine,
            new TranscriptDocument(),
            capacity: 1);
        var chunk = CreateChunk();

        Assert.True(pipeline.TryEnqueue(chunk));
        await engine.ProcessEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(pipeline.TryEnqueue(chunk));
        Assert.False(pipeline.TryEnqueue(chunk));
        Assert.Equal(1, pipeline.PendingChunkCount);
        Assert.Equal(1, pipeline.DroppedChunkCount);

        engine.AllowProcessToFinish.TrySetResult();
        await pipeline.FinalizeAsync(CancellationToken.None);
        Assert.Equal(2, engine.Events.Count(item => item == "process-start"));
        Assert.Equal("finalize", engine.Events[^1]);
    }

    private static PcmAudioChunk CreateChunk()
        => new(
            "mic",
            AudioSourceKind.Microphone,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            16000,
            1,
            [0, 0]);

    private sealed class BlockingSpeechEngine : ISpeechEngine
    {
        public string Name => "test";
        public List<string> Events { get; } = [];
        public TaskCompletionSource ProcessEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowProcessToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<IReadOnlyList<TranscriptSegment>> ProcessAudioAsync(
            PcmAudioChunk chunk,
            CancellationToken cancellationToken)
        {
            Events.Add("process-start");
            ProcessEntered.TrySetResult();
            await AllowProcessToFinish.Task.WaitAsync(cancellationToken);
            Events.Add("process-end");
            return [];
        }

        public Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(
            string wavPath,
            string sourceId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TranscriptSegment>>([]);

        public Task<IReadOnlyList<TranscriptSegment>> TranscribeFileAsync(
            string wavPath,
            string sourceId,
            IProgress<TranscriptionProgress>? progress,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TranscriptSegment>>([]);

        public Task<IReadOnlyList<TranscriptSegment>> FinalizeSessionAsync(CancellationToken cancellationToken)
        {
            Events.Add("finalize");
            return Task.FromResult<IReadOnlyList<TranscriptSegment>>([]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
