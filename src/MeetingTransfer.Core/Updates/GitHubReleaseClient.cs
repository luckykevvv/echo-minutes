using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingTransfer.Core.Updates;

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

public sealed record ReleaseInfo(
    string TagName,
    string DisplayName,
    string Notes,
    DateTimeOffset? PublishedAt,
    Uri ReleasePage,
    ReleaseAsset Package,
    ReleaseAsset Checksum);

public sealed class GitHubReleaseClient
{
    public const string Repository = "luckykevvv/echo-minutes";
    public const string PackageAssetName = "echo-minutes-win-x64.zip";
    public const string ChecksumAssetName = PackageAssetName + ".sha256";
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly Regex Sha256Pattern = new("(?i)\\b[0-9a-f]{64}\\b", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EchoMinutes", CurrentVersion.ToString(3)));
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public static Version CurrentVersion =>
        typeof(GitHubReleaseClient).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var releaseVersion = ParseVersion(tagName);
        if (releaseVersion is null || releaseVersion <= CurrentVersion)
        {
            return null;
        }

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(ParseAsset)
            .Where(asset => asset is not null)
            .Cast<ReleaseAsset>()
            .ToArray();
        var package = assets.FirstOrDefault(asset => string.Equals(asset.Name, PackageAssetName, StringComparison.OrdinalIgnoreCase));
        var checksum = assets.FirstOrDefault(asset => string.Equals(asset.Name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
        if (package is null || checksum is null)
        {
            throw new InvalidDataException($"Release {tagName} is missing {PackageAssetName} or its SHA256 file.");
        }

        if (package.Size <= 0 || package.Size > MaximumPackageBytes)
        {
            throw new InvalidDataException("The release package size is invalid.");
        }

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
        var pageUrl = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var releasePage) || releasePage.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The release page URL is invalid.");
        }

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var publishedElement) &&
            DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            publishedAt = parsedDate;
        }

        return new ReleaseInfo(
            tagName,
            string.IsNullOrWhiteSpace(name) ? tagName : name,
            string.IsNullOrWhiteSpace(notes) ? "No release notes were provided." : notes.Trim(),
            publishedAt,
            releasePage,
            package,
            checksum);
    }

    public async Task<string> DownloadAndVerifyAsync(
        ReleaseInfo release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureGitHubDownload(release.Package.DownloadUrl);
        EnsureGitHubDownload(release.Checksum.DownloadUrl);

        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "EchoMinutes",
            "updates",
            $"{SanitizeFileName(release.TagName)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, PackageAssetName);

        try
        {
            using var response = await _httpClient.GetAsync(
                release.Package.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? release.Package.Size;
            if (totalBytes <= 0 || totalBytes > MaximumPackageBytes)
            {
                throw new InvalidDataException("The downloaded package size is invalid.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[1024 * 128];
                long downloaded = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    if (downloaded > MaximumPackageBytes)
                    {
                        throw new InvalidDataException("The downloaded package exceeded the size limit.");
                    }

                    progress?.Report(Math.Clamp((double)downloaded / totalBytes, 0, 1));
                }
            }

            var checksumText = await _httpClient.GetStringAsync(release.Checksum.DownloadUrl, cancellationToken).ConfigureAwait(false);
            var expectedHash = Sha256Pattern.Match(checksumText).Value;
            if (expectedHash.Length != 64)
            {
                throw new InvalidDataException("The release checksum file is invalid.");
            }

            await using var packageStream = File.OpenRead(packagePath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update failed SHA256 verification.");
            }

            progress?.Report(1);
            return packagePath;
        }
        catch
        {
            try
            {
                Directory.Delete(updateDirectory, recursive: true);
            }
            catch
            {
                // Preserve the original download or validation exception.
            }

            throw;
        }
    }

    public static Version? ParseVersion(string? tagName)
    {
        var value = tagName?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var separator = value.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            value = value[..separator];
        }

        return Version.TryParse(value, out var version) ? version : null;
    }

    private static ReleaseAsset? ParseAsset(JsonElement element)
    {
        var name = element.GetProperty("name").GetString();
        var url = element.GetProperty("browser_download_url").GetString();
        var size = element.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
        if (string.IsNullOrWhiteSpace(name) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var downloadUrl) ||
            downloadUrl.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new ReleaseAsset(name, downloadUrl, size);
    }

    private static void EnsureGitHubDownload(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The update asset is not hosted on an approved GitHub HTTPS endpoint.");
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
