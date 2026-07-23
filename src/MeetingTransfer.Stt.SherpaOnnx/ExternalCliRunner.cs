using System.Diagnostics;
using System.Text;

namespace MeetingTransfer.Stt.SherpaOnnx;

internal static class ExternalCliRunner
{
    public static async Task<string> RunAsync(
        string executable,
        string arguments,
        Action<string>? onStderrLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {executable}.");
        var stderrBuilder = new StringBuilder();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = ReadStderrAsync(
            process.StandardError,
            stderrBuilder,
            onStderrLine,
            cancellationToken);

        string stdoutText;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stdoutText = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try { await stdoutTask.ConfigureAwait(false); } catch { }
            try { await stderrTask.ConfigureAwait(false); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stderrText = stderrBuilder.ToString();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"External speech process exited with {process.ExitCode}: {stderrText}");
        }

        // sherpa-onnx tools are inconsistent about which stream carries results.
        return string.Join(
            "\n",
            new[] { stdoutText, stderrText }.Where(value => !string.IsNullOrEmpty(value)));
    }

    private static async Task ReadStderrAsync(
        StreamReader reader,
        StringBuilder output,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            output.AppendLine(line);
            if (onLine is null)
            {
                continue;
            }

            try
            {
                onLine(line);
            }
            catch
            {
                // UI progress callbacks must not terminate or deadlock the CLI.
            }
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
            // Preserve the original cancellation or process exception.
        }
    }
}
