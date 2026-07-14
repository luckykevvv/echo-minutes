using System.Reflection;

namespace MeetingTransfer.Tests;

/// <summary>
/// StripFlag/StripFlagWithValue are private helpers that massage the whisper.cpp
/// command-line before each call. They prevent stale flags from models.json from
/// leaking into the engine's call and conflicting with the JSON output we now
/// require for fine-grained progress and structured transcript parsing.
/// </summary>
public sealed class StripFlagHelperTests
{
    private static string CallStripFlag(string args, string flag)
    {
        var method = typeof(MeetingTransfer.Stt.SherpaOnnx.SherpaOnnxSpeechEngine).GetMethod(
            "StripFlag",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StripFlag not found");
        return (string)method.Invoke(null, new object[] { args, flag })!;
    }

    private static string CallStripFlagWithValue(string args, string flag)
    {
        var method = typeof(MeetingTransfer.Stt.SherpaOnnx.SherpaOnnxSpeechEngine).GetMethod(
            "StripFlagWithValue",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StripFlagWithValue not found");
        return (string)method.Invoke(null, new object[] { args, flag })!;
    }

    [Fact]
    public void StripFlag_RemovesStandaloneFlag()
    {
        var input = "-m model.bin -f input.wav -nt -bs 1";
        var result = CallStripFlag(input, "nt");
        Assert.DoesNotContain("-nt", result);
        Assert.Contains("-m", result);
        Assert.Contains("-bs", result);
    }

    [Fact]
    public void StripFlag_RemovesFlagAtStart()
    {
        var input = "-nt -m model.bin";
        var result = CallStripFlag(input, "nt");
        Assert.DoesNotContain("-nt", result);
        Assert.Contains("-m model.bin", result);
    }

    [Fact]
    public void StripFlag_RemovesFlagAtEnd()
    {
        var input = "-m model.bin -nt";
        var result = CallStripFlag(input, "nt");
        Assert.DoesNotContain("-nt", result);
        Assert.Contains("-m model.bin", result);
    }

    [Fact]
    public void StripFlag_PreservesOtherFlagsWithSamePrefix()
    {
        // "-nt" must not eat "-nt-pp" or any other flag with the same leading letters.
        // whisper-cli doesn't actually use such compound flags, but defensive parsing
        // costs us nothing here.
        var input = "-nt-something -m model.bin";
        var result = CallStripFlag(input, "nt");
        Assert.Contains("-nt-something", result);
        Assert.Contains("-m model.bin", result);
    }

    [Fact]
    public void StripFlagWithValue_RemovesFlagAndItsArgument()
    {
        var input = "-m model.bin -of output.json -bs 1";
        var result = CallStripFlagWithValue(input, "of");
        Assert.DoesNotContain("-of", result);
        Assert.DoesNotContain("output.json", result);
        Assert.Contains("-m model.bin", result);
        Assert.Contains("-bs 1", result);
    }

    [Fact]
    public void StripFlagWithValue_HandlesMultipleOccurrences()
    {
        // Defensive: if a user model.json has two "-ml" values, we should drop both.
        var input = "-ml 80 -m model.bin -ml 40";
        var result = CallStripFlagWithValue(input, "ml");
        Assert.DoesNotContain("80", result);
        Assert.DoesNotContain("40", result);
        Assert.Contains("-m model.bin", result);
    }
}