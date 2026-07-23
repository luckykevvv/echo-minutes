using System.IO;
using System.Text;

namespace MeetingTransfer.App.Diagnostics;

public static class DiagnosticLog
{
    private const long MaximumLogBytes = 5L * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? Path => _path;

    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            _path = null;
            try
            {
                Directory.CreateDirectory(directory);
                _path = System.IO.Path.Combine(directory, "echo-minutes.log");
                RotateIfNeeded();
            }
            catch
            {
                // A user-configured diagnostics path must never prevent startup.
                _path = null;
                return;
            }
        }

        Write("INFO", $"EchoMinutes {Updates.UpdateCoordinator.CurrentVersionText} starting.");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var path = _path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:O} [{level}] {Sanitize(message)}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Diagnostics must never become the reason the application fails.
        }
    }

    private static void RotateIfNeeded()
    {
        var path = _path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= MaximumLogBytes)
        {
            return;
        }

        var previous = path + ".1";
        try { if (File.Exists(previous)) File.Delete(previous); } catch { }
        File.Move(path, previous, overwrite: true);
    }

    private static string Sanitize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
