using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MeowField.Application;
using MeowField.Infrastructure.Network;

namespace MeowField.Infrastructure.Tests;

public sealed class RemoteLibraryClientTests : IDisposable
{
    private const string TrackPath = "midi/game-music/测试曲--a1.mid";
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "meowfield-network-" + Guid.NewGuid().ToString("N"));

    private static readonly LibrarySource Primary = new("Gitee", "https://primary.test/raw/main/");
    private static readonly LibrarySource Fallback = new("GitHub", "https://fallback.test/main/");

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadCatalogAsync_UsesPrimary_AndSkipsFallback()
    {
        var handler = new StubHandler();
        var bytes = TrackBytes();
        handler.Map(Primary.CatalogUri, Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length)));
        handler.Map(Fallback.CatalogUri, Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length)));

        using var client = new RemoteLibraryClient([Primary, Fallback], _cacheDirectory, handler);
        var catalog = await client.LoadCatalogAsync(forceRefresh: false);

        Assert.False(catalog.FromCache);
        Assert.Equal("Gitee", catalog.SourceName);
        Assert.Single(catalog.Tracks);
        Assert.Equal(TrackPath, catalog.Tracks[0].Path);
        Assert.DoesNotContain(handler.Requests, url => url.StartsWith("https://fallback.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadCatalogAsync_FallsBack_WhenPrimaryFails()
    {
        var handler = new StubHandler();
        var bytes = TrackBytes();
        handler.Map(Primary.CatalogUri, Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length)), HttpStatusCode.ServiceUnavailable);
        handler.Map(Fallback.CatalogUri, Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length)));

        using var client = new RemoteLibraryClient([Primary, Fallback], _cacheDirectory, handler);
        var catalog = await client.LoadCatalogAsync(forceRefresh: false);

        Assert.Equal("GitHub", catalog.SourceName);
        Assert.Single(catalog.Tracks);
        Assert.Contains(handler.Requests, url => url.StartsWith("https://fallback.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadCatalogAsync_DecodesGzipCatalog_WhenMirrorProvidesIt()
    {
        var handler = new StubHandler();
        var bytes = TrackBytes();
        handler.Map(Primary.CompressedCatalogUri, Gzip(Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length))));

        using var client = new RemoteLibraryClient([Primary], _cacheDirectory, handler);
        var catalog = await client.LoadCatalogAsync(forceRefresh: false);

        Assert.Single(catalog.Tracks);
        Assert.Contains(handler.Requests, url => url.EndsWith("catalog.json.gz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadCatalogAsync_ReusesDiskCache_WhenEveryMirrorIsDown()
    {
        var bytes = TrackBytes();
        var first = new StubHandler();
        first.Map(Primary.CatalogUri, Catalog(BuildCatalogJson(Sha256(bytes), bytes.Length)));
        using (var warm = new RemoteLibraryClient([Primary], _cacheDirectory, first))
        {
            await warm.LoadCatalogAsync(forceRefresh: false);
        }

        var offline = new StubHandler();
        using var client = new RemoteLibraryClient([Primary, Fallback], _cacheDirectory, offline);
        var catalog = await client.LoadCatalogAsync(forceRefresh: true);

        Assert.True(catalog.FromCache);
        Assert.Equal("本地缓存", catalog.SourceName);
        Assert.Single(catalog.Tracks);
    }

    [Fact]
    public async Task LoadCatalogAsync_ThrowsLocalizedError_WhenNothingIsReachable()
    {
        using var client = new RemoteLibraryClient([Primary, Fallback], _cacheDirectory, new StubHandler());

        var exception = await Assert.ThrowsAsync<RemoteLibraryException>(() => client.LoadCatalogAsync(forceRefresh: false));

        Assert.Contains("所有镜像都没有响应", exception.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpRequestException", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadTrackAsync_ReturnsBytes_WhenChecksumMatches()
    {
        var bytes = TrackBytes();
        var handler = new StubHandler();
        handler.Map(Primary.TrackUri(TrackPath), bytes);

        using var client = new RemoteLibraryClient([Primary], _cacheDirectory, handler);
        var downloaded = await client.DownloadTrackAsync(Track(bytes, Sha256(bytes)));

        Assert.Equal(bytes, downloaded);
    }

    [Fact]
    public async Task DownloadTrackAsync_SkipsMirrorWithWrongChecksum()
    {
        var tampered = Encoding.UTF8.GetBytes("tampered-content");
        var expected = TrackBytes();
        var handler = new StubHandler();
        handler.Map(Primary.TrackUri(TrackPath), tampered);
        handler.Map(Fallback.TrackUri(TrackPath), expected);

        using var client = new RemoteLibraryClient([Primary, Fallback], _cacheDirectory, handler);
        var downloaded = await client.DownloadTrackAsync(Track(expected, Sha256(expected)));

        Assert.Equal(expected, downloaded);
    }

    [Fact]
    public async Task DownloadTrackAsync_ThrowsLocalizedError_WhenChecksumNeverMatches()
    {
        var expected = TrackBytes();
        var handler = new StubHandler();
        handler.Map(Primary.TrackUri(TrackPath), Encoding.UTF8.GetBytes("tampered-content"));

        using var client = new RemoteLibraryClient([Primary], _cacheDirectory, handler);
        var exception = await Assert.ThrowsAsync<RemoteLibraryException>(
            () => client.DownloadTrackAsync(Track(expected, Sha256(expected))));

        Assert.Contains("下载失败", exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackUri_KeepsPathSeparators_AndEncodesUnicode()
    {
        var uri = Primary.TrackUri("midi/动漫与 ACG/【Animenz】测试--ab12.mid");
        var text = uri.AbsoluteUri;

        Assert.Contains("/midi/", text, StringComparison.Ordinal);
        Assert.Contains("%E5%8A%A8%E6%BC%AB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("%2F", text, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] TrackBytes() => Encoding.UTF8.GetBytes("MThd\u0000\u0000\u0000\u0006\u0000\u0000");

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static RemoteLibraryTrack Track(byte[] bytes, string sha) =>
        new("a1", "测试曲", "game-music", TrackPath, sha, bytes.Length);

    private static string BuildCatalogJson(string sha, long length) =>
        $$"""
        {"schemaVersion":1,"generatedAt":"2026-10-20T00:00:00Z","tracks":[
          {"id":"a1","name":"测试曲","category":"game-music","path":"{{TrackPath}}","sha256":"{{sha}}","bytes":{{length}}}
        ]}
        """;

    private static byte[] Catalog(string json) => Encoding.UTF8.GetBytes(json);

    private static byte[] Gzip(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(payload);
        }

        return output.ToArray();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Payload)> _responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public void Map(Uri uri, byte[] payload, HttpStatusCode status = HttpStatusCode.OK) =>
            _responses[uri.AbsoluteUri] = (status, payload);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            if (!_responses.TryGetValue(url, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new ByteArrayContent(response.Payload),
            });
        }
    }
}
