using System.Diagnostics;

namespace MeetingTransfer.Core.Import;

public sealed class MediaImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".mp4",
        ".mkv",
        ".mov"
    };

    public const string BuiltInFfmpegRelativePath = "models/ffmpeg/bin/ffmpeg.exe";

    public bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    public string? ResolveFfmpegPath(string? configuredPath, string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (!string.IsNullOrWhiteSpace(configuredPath) && !Path.IsPathRooted(configuredPath))
        {
            var rooted = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, configuredPath);
            if (File.Exists(rooted))
            {
                return rooted;
            }
        }

        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var builtIn = Path.Combine(baseDir, BuiltInFfmpegRelativePath);
        return File.Exists(builtIn) ? builtIn : null;
    }

    public async Task<string> ExtractAudioAsync(
        string inputPath,
        string? ffmpegPath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input media file does not exist.", inputPath);
        }

        if (!IsSupported(inputPath))
        {
            throw new NotSupportedException($"Unsupported media extension: {Path.GetExtension(inputPath)}");
        }

        var resolvedFfmpeg = ResolveFfmpegPath(ffmpegPath);
        if (resolvedFfmpeg is null)
        {
            throw new FileNotFoundException(
                "ffmpeg.exe was not found. It is bundled inside the app under 'models/ffmpeg/bin/ffmpeg.exe'. " +
                "If you replaced it on purpose, configure a working path in Settings > ffmpeg.exe.",
                ffmpegPath ?? BuiltInFfmpegRelativePath);
        }

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(inputPath)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.wav");

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-y", "-i", inputPath, "-vn", "-ac", "1", "-ar", "16000",
            "-sample_fmt", "s16", outputPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start ffmpeg.");

        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var error = await errorTask.ConfigureAwait(false);
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {error}");
            }

            return outputPath;
        }
        catch
        {
            TryKill(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort child process cleanup during cancellation/failure.
        }
    }
}
