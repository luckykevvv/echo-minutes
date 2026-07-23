using System.Reflection;
using MeetingTransfer.Stt.SherpaOnnx;

namespace MeetingTransfer.Tests;

public sealed class WhisperSentenceSplittingTests
{
    // Internal record struct mirror of SherpaOnnxSpeechEngine.WhisperRawSegment.
    // We rebuild it locally rather than expose WhisperRawSegment publicly, so the
    // engine's internal data shape can keep evolving.
    private readonly record struct InternalRawSegment(long StartMs, long EndMs, string Text);

    private static IReadOnlyList<InternalRawSegment> Split(
        string[] texts,
        int[] startMs,
        int[] endMs,
        int maxLen = 80,
        bool splitOnWord = true,
        double maxSeconds = 6.0)
    {
        if (texts.Length != startMs.Length || texts.Length != endMs.Length)
        {
            throw new ArgumentException("texts / startMs / endMs length mismatch");
        }
        var raw = new List<InternalRawSegment>(texts.Length);
        for (int i = 0; i < texts.Length; i++)
        {
            raw.Add(new InternalRawSegment(startMs[i], endMs[i], texts[i]));
        }

        // Find SherpaOnnxSpeechEngine's internal WhisperRawSegment type by structural
        // shape: a generic record struct with three public fields (long StartMs, long
        // EndMs, string Text). We construct a list of it via ArrayList so we don't
        // have to take a compile-time dependency on the engine's private type.
        var engineType = typeof(SherpaOnnxSpeechEngine);
        var rawSegmentType = engineType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(t => t.Name.Contains("WhisperRawSegment"))
            ?? throw new InvalidOperationException("WhisperRawSegment not found");
        var rawListType = typeof(List<>).MakeGenericType(rawSegmentType);
        var rawList = Activator.CreateInstance(rawListType)!;
        var addMethod = rawListType.GetMethod("Add")!;
        var rawCtor = rawSegmentType.GetConstructor(new[] { typeof(long), typeof(long), typeof(string) })
            ?? throw new InvalidOperationException("WhisperRawSegment(long, long, string) ctor not found");
        foreach (var seg in raw)
        {
            var instance = rawCtor.Invoke(new object[] { seg.StartMs, seg.EndMs, seg.Text });
            addMethod.Invoke(rawList, new[] { instance });
        }

        var splitMethod = engineType.GetMethod(
            "SplitIntoSentences",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SplitIntoSentences not found");

        var resultObject = splitMethod.Invoke(null, new object[] { rawList, maxLen, splitOnWord, maxSeconds })!;
        var resultList = new List<InternalRawSegment>();
        foreach (var item in (System.Collections.IEnumerable)resultObject)
        {
            var itemType = item.GetType();
            // record struct fields are exposed as init-only properties, not public
            // fields, so we read both and prefer whichever is populated.
            object GetMember(string name)
            {
                var p = itemType.GetProperty(name);
                if (p is not null)
                {
                    return p.GetValue(item)!;
                }
                var f = itemType.GetField(name);
                return f?.GetValue(item)!;
            }
            resultList.Add(new InternalRawSegment(
                (long)GetMember("StartMs"),
                (long)GetMember("EndMs"),
                (string)GetMember("Text")));
        }
        return resultList;
    }

    [Fact]
    public void Split_BreaksAtSentencePunctuation()
    {
        // Three whisper-cli chunks that already end in `.`, `?`, `!` should each
        // land in their own segment — this is the single-speaker-friendly default.
        var split = Split(
            texts: new[] { "Hi everyone.", "How are you?", "Great!" },
            startMs: new[] { 0, 1500, 3000 },
            endMs: new[] { 1500, 3000, 4500 });

        Assert.Equal(3, split.Count);
        Assert.Equal("Hi everyone.", split[0].Text);
        Assert.Equal("How are you?", split[1].Text);
        Assert.Equal("Great!", split[2].Text);
    }

    [Fact]
    public void Split_JoinsChunksUntilSentenceEnd()
    {
        // Three consecutive chunks that do NOT end in punctuation should merge
        // into a single segment, regardless of internal punctuation.
        var split = Split(
            texts: new[] { "Hi everyone,", "I'm Jia Wen,", "welcome." },
            startMs: new[] { 0, 1500, 3000 },
            endMs: new[] { 1500, 3000, 4500 });

        Assert.Single(split);
        Assert.Equal("Hi everyone, I'm Jia Wen, welcome.", split[0].Text);
        Assert.Equal(0, split[0].StartMs);
        Assert.Equal(4500, split[0].EndMs);
    }

    [Fact]
    public void Split_BreaksOnLongSilenceGap()
    {
        // 800 ms silence between two non-punctuated chunks is treated as a sentence
        // boundary (700 ms threshold). This catches the "person thought, then
        // continued" case in a single-speaker stream.
        var split = Split(
            texts: new[] { "first thought and", "second thought" },
            startMs: new[] { 0, 2300 },
            endMs: new[] { 1500, 3000 });

        Assert.Equal(2, split.Count);
        Assert.Equal("first thought and", split[0].Text);
        Assert.Equal("second thought", split[1].Text);
    }

    [Fact]
    public void Split_ForcesSegmentWhenMaxLenExceeded()
    {
        // Five short chunks with no punctuation and tiny gaps — would naturally
        // all merge, but maxLen forces a break mid-stream.
        var split = Split(
            texts: new[] {
                "alpha bravo charlie delta",
                "echo foxtrot golf",
                "hotel india juliet",
                "kilo lima mike",
                "november oscar",
            },
            startMs: new[] { 0, 1000, 2000, 3000, 4000 },
            endMs: new[] { 1000, 2000, 3000, 4000, 5000 },
            maxLen: 30,
            splitOnWord: true);

        // Each chunk except the last is <30 chars, so they can pack into one
        // ~100-char segment, but the maxLen limit forces a flush.
        Assert.True(split.Count >= 2);
        foreach (var seg in split)
        {
            Assert.True(seg.Text.Length <= 35,
                $"Segment too long: '{seg.Text}' ({seg.Text.Length} chars)");
        }
    }

    [Fact]
    public void Split_HandlesChineseAndMixedText()
    {
        // Chinese full stop 。 should be treated like English . — the same
        // SplitIntoSentences code path handles both.
        var split = Split(
            texts: new[] { "今天是星期三。", "明天是星期四。" },
            startMs: new[] { 0, 1500 },
            endMs: new[] { 1500, 3000 });

        Assert.Equal(2, split.Count);
        Assert.Equal("今天是星期三。", split[0].Text);
        Assert.Equal("明天是星期四。", split[1].Text);
    }

    [Fact]
    public void Split_EnforcesMaximumSegmentDuration()
    {
        var split = Split(
            texts: ["one", "two", "three", "four"],
            startMs: [0, 2000, 4000, 6000],
            endMs: [2000, 4000, 6000, 8000],
            maxLen: 200,
            maxSeconds: 3.0);

        Assert.Equal(4, split.Count);
        Assert.All(split, segment =>
            Assert.InRange(segment.EndMs - segment.StartMs, 0, 3000));
        Assert.Equal(["one", "two", "three", "four"], split.Select(segment => segment.Text));
    }

    [Fact]
    public void Split_SkipsEmptyChunksAndContinues()
    {
        // whisper-cli emits empty chunks for silence. The splitter should treat
        // them as "still inside the previous sentence" unless a long silence
        // follows.
        var split = Split(
            texts: new[] { "first sentence", "", "second sentence" },
            startMs: new[] { 0, 1000, 1100 },
            endMs: new[] { 1000, 1000, 2000 });

        Assert.Single(split);
        Assert.Contains("first sentence", split[0].Text);
        Assert.Contains("second sentence", split[0].Text);
    }

    [Fact]
    public void EndsWithSentencePunctuation_DetectsEnglishAndChinese()
    {
        var method = typeof(SherpaOnnxSpeechEngine).GetMethod(
            "EndsWithSentencePunctuation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.True((bool)method.Invoke(null, new object[] { "hello." })!);
        Assert.True((bool)method.Invoke(null, new object[] { "really?" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "wow!" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "你好。" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "什么？" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "啊！" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "等等…" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "He said \"hi.\"" })!);

        Assert.False((bool)method.Invoke(null, new object[] { "and" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "no terminal" })!);
    }
}
