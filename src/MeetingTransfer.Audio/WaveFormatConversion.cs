using NAudio.Wave;

namespace MeetingTransfer.Audio;

internal static class WaveFormatConversion
{
    public static byte[] ToPcm16Mono(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat, int targetSampleRate)
    {
        using var input = new RawSourceWaveStream(
            new MemoryStream(buffer, 0, bytesRecorded, writable: false),
            sourceFormat);
        var targetFormat = new WaveFormat(targetSampleRate, 16, 1);
        using var resampler = new MediaFoundationResampler(input, targetFormat)
        {
            ResamplerQuality = 60
        };
        using var output = new MemoryStream();
        var scratch = new byte[targetFormat.AverageBytesPerSecond];
        int read;
        while ((read = resampler.Read(scratch, 0, scratch.Length)) > 0)
        {
            output.Write(scratch, 0, read);
        }

        return output.ToArray();
    }
}
