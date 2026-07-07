using System.Net;
using iFakeLocation.Services.DeveloperImages;

namespace iFakeLocation.Tests.Services.DeveloperImages;

file sealed class NeverRespondingHandler : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}

public class DownloadStateStoreTests {
    [Fact]
    public void GetOrStart_CalledTwiceForSameVersion_OnlyInvokesFactoryOnce() {
        var store = new DownloadStateStore();
        var factoryCallCount = 0;
        using var client = new HttpClient(new NeverRespondingHandler());

        DownloadState Factory() {
            factoryCallCount++;
            return new DownloadState(client, ["http://example.invalid/x.dmg"], [Path.GetTempFileName()]);
        }

        var first = store.GetOrStart("16.4", Factory);
        var second = store.GetOrStart("16.4", Factory);

        Assert.Same(first, second);
        Assert.Equal(1, factoryCallCount);
    }

    [Fact]
    public void GetOrStart_DifferentVersions_CreatesSeparateStates() {
        var store = new DownloadStateStore();
        using var client = new HttpClient(new NeverRespondingHandler());

        var a = store.GetOrStart("16.4", () => new DownloadState(client, ["http://example.invalid/a"], [Path.GetTempFileName()]));
        var b = store.GetOrStart("17.0", () => new DownloadState(client, ["http://example.invalid/b"], [Path.GetTempFileName()]));

        Assert.NotSame(a, b);
    }

    [Fact]
    public void TryGet_UnknownVersion_ReturnsFalse() {
        var store = new DownloadStateStore();
        Assert.False(store.TryGet("99.0", out _));
    }
}
