using System.Net;
using System.Text;
using iFakeLocation.Services.DeveloperImages;

namespace iFakeLocation.Tests.Services.DeveloperImages;

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

public class DownloadStateTests : IDisposable {
    private readonly string _tempDir = Directory.CreateTempSubdirectory("ifl-download-tests-").FullName;

    public void Dispose() {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static HttpClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new FakeHttpMessageHandler(responder));

    [Fact]
    public async Task RunAsync_SingleFile_DownloadsAndMarksDone() {
        var content = Encoding.UTF8.GetBytes("hello world, this is fake file content");
        using var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(content),
        });

        var destination = Path.Combine(_tempDir, "file.bin");
        var state = new DownloadState(client, ["http://example.invalid/file.bin"], [destination]);

        await state.RunAsync();

        Assert.True(state.Done);
        Assert.Null(state.Error);
        Assert.True(File.Exists(destination));
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".incomplete"));
    }

    [Fact]
    public async Task RunAsync_MultipleFiles_DownloadsSequentiallyInOrder() {
        var downloadedUrls = new List<string>();
        using var client = MakeClient(request => {
            downloadedUrls.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });

        var links = new[] { "http://example.invalid/a.dmg", "http://example.invalid/a.dmg.signature" };
        var paths = new[] { Path.Combine(_tempDir, "a.dmg"), Path.Combine(_tempDir, "a.dmg.signature") };
        var state = new DownloadState(client, links, paths);

        await state.RunAsync();

        Assert.True(state.Done);
        Assert.Equal(links, downloadedUrls);
        Assert.All(paths, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public async Task RunAsync_HttpFailure_SetsErrorAndDoesNotMarkDone() {
        using var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var destination = Path.Combine(_tempDir, "missing.bin");
        var state = new DownloadState(client, ["http://example.invalid/missing.bin"], [destination]);

        await state.RunAsync();

        Assert.False(state.Done);
        Assert.NotNull(state.Error);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Constructor_MismatchedLinksAndPaths_Throws() {
        using var client = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Throws<ArgumentException>(() => new DownloadState(client, ["a", "b"], ["only-one"]));
    }
}
