using System.Net;
using System.Security.Cryptography;
using System.Text;
using MeetingTransfer.Core.Updates;

namespace MeetingTransfer.Tests;

public sealed class GitHubReleaseClientTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("2.0.0-beta.1", 2, 0, 0)]
    [InlineData("V3.4.5+build.7", 3, 4, 5)]
    public void ParseVersionAcceptsReleaseTags(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), GitHubReleaseClient.ParseVersion(tag));
    }

    [Fact]
    public async Task CheckForUpdateReturnsReleaseWithPackageAndChecksum()
    {
        var json = """
            {
              "tag_name": "v9.0.0",
              "name": "EchoMinutes 9.0",
              "body": "Faster startup and a clearer update screen.",
              "published_at": "2026-07-15T00:00:00Z",
              "html_url": "https://github.com/luckykevvv/echo-minutes/releases/tag/v9.0.0",
              "assets": [
                {
                  "name": "echo-minutes-win-x64.zip",
                  "browser_download_url": "https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip",
                  "size": 1234
                },
                {
                  "name": "echo-minutes-win-x64.zip.sha256",
                  "browser_download_url": "https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip.sha256",
                  "size": 96
                }
              ]
            }
            """;
        using var httpClient = new HttpClient(new StubHandler(_ => Json(json)));
        var client = new GitHubReleaseClient(httpClient);

        var release = await client.CheckForUpdateAsync();

        Assert.NotNull(release);
        Assert.Equal("v9.0.0", release.TagName);
        Assert.Equal(GitHubReleaseClient.PackageAssetName, release.Package.Name);
        Assert.Equal(GitHubReleaseClient.ChecksumAssetName, release.Checksum.Name);
        Assert.Contains("Faster startup", release.Notes);
    }

    [Fact]
    public async Task DownloadRejectsPackageWhenChecksumDoesNotMatch()
    {
        var package = Encoding.UTF8.GetBytes("not a real release package");
        var release = CreateRelease(package.Length);
        using var httpClient = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Text(new string('0', 64))
                : Bytes(package)));
        var client = new GitHubReleaseClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAndVerifyAsync(release));

        Assert.Contains("SHA256", exception.Message);
    }

    [Fact]
    public async Task DownloadAcceptsPackageWithMatchingChecksum()
    {
        var package = Encoding.UTF8.GetBytes("verified release package");
        var hash = Convert.ToHexString(SHA256.HashData(package));
        var release = CreateRelease(package.Length);
        using var httpClient = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Text($"{hash}  {GitHubReleaseClient.PackageAssetName}")
                : Bytes(package)));
        var client = new GitHubReleaseClient(httpClient);

        var path = await client.DownloadAndVerifyAsync(release);
        try
        {
            Assert.Equal(package, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRejectsOversizedChecksumResponse()
    {
        var package = Encoding.UTF8.GetBytes("verified release package");
        var release = CreateRelease(package.Length);
        using var httpClient = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                ? Text(new string('a', 20 * 1024))
                : Bytes(package)));
        var client = new GitHubReleaseClient(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.DownloadAndVerifyAsync(release));

        Assert.Contains("too large", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ReleaseInfo CreateRelease(long size) => new(
        "v9.0.0",
        "EchoMinutes 9.0",
        "Notes",
        DateTimeOffset.UtcNow,
        new Uri("https://github.com/luckykevvv/echo-minutes/releases/tag/v9.0.0"),
        new ReleaseAsset(GitHubReleaseClient.PackageAssetName, new Uri("https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip"), size),
        new ReleaseAsset(GitHubReleaseClient.ChecksumAssetName, new Uri("https://github.com/luckykevvv/echo-minutes/releases/download/v9.0.0/echo-minutes-win-x64.zip.sha256"), 96));

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "text/plain")
    };

    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(value)
        {
            Headers = { ContentLength = value.Length }
        }
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
