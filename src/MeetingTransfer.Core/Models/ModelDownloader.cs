using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MeetingTransfer.Core.Models;

public sealed class ModelDownloader
{
    private const int MaxAttempts = 3;
    private const long MinimumDownloadLimitBytes = 512L * 1024 * 1024;
    private const long DefaultDownloadLimitBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumDownloadLimitBytes = 8L * 1024 * 1024 * 1024;
    private readonly HttpClient _http;

    public ModelDownloader(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        if (_http.Timeout == System.Threading.Timeout.InfiniteTimeSpan || _http.Timeout.TotalMinutes < 5)
        {
            _http.Timeout = TimeSpan.FromMinutes(30);
        }
    }

    /// <summary>
    /// Downloads all files for a model into its model directory.
    /// Reports progress in [0, 1] across the whole bundle.
    /// </summary>
    public async Task DownloadAsync(
        ModelDescriptor model,
        ModelCatalog catalog,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (model.Files.Count == 0)
        {
            return;
        }

        var dir = catalog.GetModelDirectory(model);
        Directory.CreateDirectory(dir);
        var maximumDownloadBytes = GetMaximumDownloadBytes(model.SizeBytes);

        // Pre-count total bytes for an accurate progress bar.
        long totalBytes = 0;
        long[] sizes = new long[model.Files.Count];
        for (int i = 0; i < model.Files.Count; i++)
        {
            var uri = GetValidatedUri(model.Files[i].Url);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var head = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (head.IsSuccessStatusCode && head.Content.Headers.ContentLength is long len)
                {
                    sizes[i] = len;
                    totalBytes += len;
                }
            }
            catch
            {
                // If HEAD fails, we'll just fall back to per-file indeterminate progress.
            }
        }
        if (totalBytes > maximumDownloadBytes)
        {
            throw new InvalidDataException(
                $"Model download is larger than the allowed limit of {maximumDownloadBytes} bytes.");
        }
        if (totalBytes == 0)
        {
            totalBytes = model.SizeBytes;
        }

        long downloaded = 0;
        for (int i = 0; i < model.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = model.Files[i];
            var dest = catalog.GetInstalledFilePath(model, file);
            if (File.Exists(dest) && await IsExistingFileValidAsync(file, dest, sizes[i], cancellationToken).ConfigureAwait(false))
            {
                downloaded += sizes[i] > 0 ? sizes[i] : new FileInfo(dest).Length;
                progress?.Report(totalBytes > 0 ? (double)downloaded / totalBytes : 1.0);
                continue;
            }

            var tempPath = dest + ".part";
            var downloadedBeforeFile = downloaded;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var uri = GetValidatedUri(file.Url);

                    using var resp = await _http.GetAsync(
                        uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    if (resp.Content.Headers.ContentLength is long responseLength &&
                        responseLength > maximumDownloadBytes - downloadedBeforeFile)
                    {
                        throw new InvalidDataException(
                            $"Model download is larger than the allowed limit of {maximumDownloadBytes} bytes.");
                    }

                    await using (var src = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[64 * 1024];
                        int read;
                        long fileDownloaded = 0;
                        while ((read = await src.ReadAsync(buf, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            fileDownloaded += read;
                            downloaded = downloadedBeforeFile + fileDownloaded;
                            if (downloaded > maximumDownloadBytes)
                            {
                                throw new InvalidDataException(
                                    $"Model download exceeded the allowed limit of {maximumDownloadBytes} bytes.");
                            }
                            if (totalBytes > 0)
                            {
                                progress?.Report(Math.Min(1.0, (double)downloaded / totalBytes));
                            }
                        }
                    }

                    if (file.Extract is not null)
                    {
                        ExtractSingleFile(tempPath, file.Extract, maximumDownloadBytes);
                    }

                    if (!string.IsNullOrWhiteSpace(file.Sha256))
                    {
                        var actual = await ComputeSha256Async(tempPath, cancellationToken).ConfigureAwait(false);
                        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Downloaded file '{file.Name}' failed checksum verification. " +
                                $"Expected {file.Sha256}, got {actual}.");
                        }
                    }

                    File.Move(tempPath, dest, overwrite: true);
                    break;
                }
                catch (Exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    downloaded = downloadedBeforeFile;
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }
            }
        }

        progress?.Report(1.0);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<bool> IsExistingFileValidAsync(
        ModelFileEntry file,
        string path,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        return expectedSize > 0 && new FileInfo(path).Length == expectedSize;
    }

    private static Uri GetValidatedUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Model download URL must be an absolute HTTPS URL: {value}");
        }

        return uri;
    }

    private static long GetMaximumDownloadBytes(long declaredSizeBytes)
    {
        if (declaredSizeBytes <= 0)
        {
            return DefaultDownloadLimitBytes;
        }

        var doubled = declaredSizeBytes > MaximumDownloadLimitBytes / 2
            ? MaximumDownloadLimitBytes
            : declaredSizeBytes * 2;
        return Math.Clamp(doubled, MinimumDownloadLimitBytes, MaximumDownloadLimitBytes);
    }

    private static void ExtractSingleFile(
        string archivePath,
        ModelFileExtract extract,
        long maximumExtractedBytes)
    {
        if (!string.Equals(extract.Format, "tar.bz2", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported model archive format: {extract.Format}");
        }

        var requestedMember = NormalizeArchivePath(extract.Member);
        if (string.IsNullOrWhiteSpace(requestedMember) ||
            requestedMember.StartsWith("../", StringComparison.Ordinal) ||
            requestedMember.Contains("/../", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsafe model archive member: '{extract.Member}'.");
        }

        var extractedPath = archivePath + ".extracted";
        try
        {
            var found = false;
            using (var archiveStream = File.OpenRead(archivePath))
            using (var reader = ReaderFactory.Open(archiveStream))
            {
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory ||
                        !string.Equals(NormalizeArchivePath(entry.Key ?? string.Empty), requestedMember, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (entry.Size < 0 || entry.Size > maximumExtractedBytes)
                    {
                        throw new InvalidDataException(
                            $"Model archive member exceeds the allowed extracted size of {maximumExtractedBytes} bytes.");
                    }

                    reader.WriteEntryToFile(extractedPath, new ExtractionOptions
                    {
                        ExtractFullPath = false,
                        Overwrite = true
                    });
                    found = true;
                    break;
                }
            }

            if (File.Exists(extractedPath) && new FileInfo(extractedPath).Length > maximumExtractedBytes)
            {
                throw new InvalidDataException(
                    $"Extracted model file exceeds the allowed size of {maximumExtractedBytes} bytes.");
            }

            if (!found)
            {
                throw new InvalidDataException(
                    $"Model archive does not contain required member '{extract.Member}'.");
            }

            File.Move(extractedPath, archivePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(extractedPath)) File.Delete(extractedPath); } catch { }
        }
    }

    private static string NormalizeArchivePath(string value)
        => value.Replace('\\', '/').TrimStart('/');
}
