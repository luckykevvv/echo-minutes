using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MeetingTransfer.Core.Models;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace MeetingTransfer.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "mt-downloader-tests-" + Guid.NewGuid().ToString("N"));

    public ModelDownloaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DownloadAsync_ReplacesSameSizeFileWithWrongChecksum()
    {
        byte[] expected = [1, 2, 3];
        var model = MakeModel(expected);
        var catalog = new ModelCatalog(_tempDir);
        var path = catalog.GetInstalledFilePath(model, model.Files[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [9, 9, 9]);
        var handler = new DownloadHandler(expected);

        await new ModelDownloader(new HttpClient(handler)).DownloadAsync(
            model, catalog, progress: null, CancellationToken.None);

        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, handler.GetCount);
    }

    [Fact]
    public async Task DownloadAsync_SkipsExistingFileWithMatchingChecksum()
    {
        byte[] expected = [4, 5, 6];
        var model = MakeModel(expected);
        var catalog = new ModelCatalog(_tempDir);
        var path = catalog.GetInstalledFilePath(model, model.Files[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, expected);
        var handler = new DownloadHandler(expected);

        await new ModelDownloader(new HttpClient(handler)).DownloadAsync(
            model, catalog, progress: null, CancellationToken.None);

        Assert.Equal(0, handler.GetCount);
    }

    [Fact]
    public async Task DownloadAsync_RetriesTransientServerFailure()
    {
        byte[] expected = [7, 8, 9];
        var model = MakeModel(expected);
        var catalog = new ModelCatalog(_tempDir);
        var handler = new DownloadHandler(expected, failuresBeforeSuccess: 1);

        await new ModelDownloader(new HttpClient(handler)).DownloadAsync(
            model, catalog, progress: null, CancellationToken.None);

        Assert.Equal(2, handler.GetCount);
        Assert.Equal(expected, await File.ReadAllBytesAsync(catalog.GetInstalledFilePath(model, model.Files[0])));
    }

    [Fact]
    public async Task DownloadAsync_ExtractsOnlyConfiguredTarBz2Member()
    {
        byte[] expected = [10, 20, 30, 40];
        var archiveBytes = CreateTarBz2("bundle/model.int8.onnx", expected);
        var model = new ModelDescriptor
        {
            Id = "archive-model",
            SizeBytes = expected.Length,
            Files =
            [
                new ModelFileEntry
                {
                    Name = "model.int8.onnx",
                    Url = "https://example.com/model.tar.bz2",
                    Sha256 = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(),
                    Extract = new ModelFileExtract
                    {
                        Format = "tar.bz2",
                        Member = "bundle/model.int8.onnx"
                    }
                }
            ]
        };
        var catalog = new ModelCatalog(_tempDir);

        await new ModelDownloader(new HttpClient(new DownloadHandler(archiveBytes))).DownloadAsync(
            model, catalog, progress: null, CancellationToken.None);

        Assert.Equal(expected, await File.ReadAllBytesAsync(catalog.GetInstalledFilePath(model, model.Files[0])));
    }

    [Fact]
    public async Task DownloadAsync_CancellationRemovesPartialFile()
    {
        byte[] content = [11, 22, 33, 44, 55, 66];
        var model = MakeModel(content);
        var catalog = new ModelCatalog(_tempDir);
        using var cancellation = new CancellationTokenSource();
        var handler = new CancellingDownloadHandler(content, cancellation);
        var destination = catalog.GetInstalledFilePath(model, model.Files[0]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ModelDownloader(new HttpClient(handler)).DownloadAsync(
                model, catalog, progress: null, cancellation.Token));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_RejectsUnexpectedlyLargeBundleBeforeGet()
    {
        byte[] content = [1, 2, 3];
        var model = MakeModel(content);
        var catalog = new ModelCatalog(_tempDir);
        var handler = new DownloadHandler(content, reportedLength: 600L * 1024 * 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ModelDownloader(new HttpClient(handler)).DownloadAsync(
                model, catalog, progress: null, CancellationToken.None));

        Assert.Contains("allowed limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.GetCount);
    }

    private static ModelDescriptor MakeModel(byte[] content)
        => new()
        {
            Id = "test-model",
            SizeBytes = content.Length,
            Files =
            [
                new ModelFileEntry
                {
                    Name = "model.bin",
                    Url = "https://example.com/model.bin",
                    Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
                }
            ]
        };

    private static byte[] CreateTarBz2(string member, byte[] content)
    {
        using var output = new MemoryStream();
        using (var writer = WriterFactory.Open(
            output,
            ArchiveType.Tar,
            new WriterOptions(CompressionType.BZip2)
            {
                LeaveStreamOpen = true
            }))
        {
            writer.Write(member, new MemoryStream(content));
        }
        return output.ToArray();
    }

    private sealed class DownloadHandler(
        byte[] content,
        int failuresBeforeSuccess = 0,
        long? reportedLength = null) : HttpMessageHandler
    {
        public int GetCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                head.Content.Headers.ContentLength = reportedLength ?? content.Length;
                return Task.FromResult(head);
            }

            GetCount++;
            if (GetCount <= failuresBeforeSuccess)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class CancellingDownloadHandler(
        byte[] content,
        CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                var head = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                head.Content.Headers.ContentLength = content.Length;
                return Task.FromResult(head);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new CancelAfterFirstReadStream(content, cancellation))
            });
        }
    }

    private sealed class CancelAfterFirstReadStream(
        byte[] content,
        CancellationTokenSource cancellation) : MemoryStream(content)
    {
        private bool _cancelled;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var read = base.ReadAsync(buffer, cancellationToken);
            if (!_cancelled)
            {
                _cancelled = true;
                cancellation.Cancel();
            }

            return read;
        }
    }
}
