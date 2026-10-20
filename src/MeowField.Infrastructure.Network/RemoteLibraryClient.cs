using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using MeowField.Application;
using Microsoft.Extensions.Logging;

namespace MeowField.Infrastructure.Network;

/// <summary>
/// 按 <see cref="LibrarySources"/> 的顺序逐个尝试镜像；任一成功即返回。
/// 目录成功拉取后写本地缓存，下次启动先读缓存，避免每次都重复下载数 MB 索引。
/// </summary>
public sealed class RemoteLibraryClient : IRemoteLibraryClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // 单次请求上限。raw.githubusercontent.com 在国内常常是「连接挂着不回」，
    // 没有这一层就会一直等到系统 TCP 超时，界面看起来像卡死。
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly IReadOnlyList<LibrarySource> _sources;
    private readonly string _cacheDirectory;
    private readonly ILogger<RemoteLibraryClient>? _logger;
    private readonly HashSet<string> _gzipRejected = new(StringComparer.Ordinal);
    private RemoteLibraryCatalog? _memo;

    public RemoteLibraryClient(
        IReadOnlyList<LibrarySource> sources,
        string cacheDirectory,
        ILogger<RemoteLibraryClient>? logger = null)
        : this(sources, cacheDirectory, CreateHandler(), logger)
    {
    }

    internal RemoteLibraryClient(
        IReadOnlyList<LibrarySource> sources,
        string cacheDirectory,
        HttpMessageHandler handler,
        ILogger<RemoteLibraryClient>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);
        _sources = sources;
        _cacheDirectory = cacheDirectory;
        _logger = logger;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MeowField_AutoPiano");
    }

    public async Task<RemoteLibraryCatalog> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _memo is not null)
        {
            return _memo;
        }

        if (!forceRefresh)
        {
            var cached = await TryReadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                _memo = cached;
                return cached;
            }
        }

        var failures = new List<string>();
        foreach (var source in _sources)
        {
            try
            {
                var tracks = await FetchCatalogAsync(source, cancellationToken).ConfigureAwait(false);
                var catalog = new RemoteLibraryCatalog(tracks, FromCache: false, source.Name);
                _memo = catalog;
                await WriteCacheAsync(tracks, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("曲库目录已从 {Source} 加载，共 {Count} 首", source.Name, tracks.Count);
                return catalog;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "曲库目录从 {Source} 加载失败", source.Name);
                failures.Add($"{source.Name} ({source.BaseUrl}): {exception.Message}");
            }
        }

        // 全部镜像失败时不直接报错：有缓存就用缓存，至少让用户还能浏览曲目列表。
        var fallback = await TryReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (fallback is not null)
        {
            _memo = fallback;
            return fallback;
        }

        throw new RemoteLibraryException(
            "曲库目录加载失败，所有镜像都没有响应。请检查网络连接后点「刷新曲库」重试。",
            new AggregateException(failures.Select(message => new InvalidOperationException(message))));
    }

    public async Task<byte[]> DownloadTrackAsync(RemoteLibraryTrack track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        var failures = new List<string>();
        foreach (var source in _sources)
        {
            try
            {
                var bytes = await GetAsync(source.TrackUri(track.Path), cancellationToken).ConfigureAwait(false);
                if (!Verify(track, bytes))
                {
                    // 校验不过说明镜像文件损坏或被替换，换下一个源，不要交给播放器。
                    _logger?.LogWarning("{Source} 上的 {Track} 校验不通过", source.Name, track.Name);
                    failures.Add($"{source.Name}: 校验不通过");
                    continue;
                }

                return bytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "曲目 {Track} 从 {Source} 下载失败", track.Name, source.Name);
                failures.Add($"{source.Name}: {exception.Message}");
            }
        }

        throw new RemoteLibraryException(
            $"「{track.Name}」下载失败，所有镜像都没有响应。请稍后重试。",
            new AggregateException(failures.Select(message => new InvalidOperationException(message))));
    }

    public void Dispose() => _http.Dispose();

    private async Task<IReadOnlyList<RemoteLibraryTrack>> FetchCatalogAsync(LibrarySource source, CancellationToken cancellationToken)
    {
        byte[] payload;
        if (!_gzipRejected.Contains(source.BaseUrl))
        {
            try
            {
                payload = await GetAsync(source.CompressedCatalogUri, cancellationToken).ConfigureAwait(false);
                payload = DecompressIfGzip(payload);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // 镜像没有生成 .gz 时记下来，本次会话不再重试。
                _gzipRejected.Add(source.BaseUrl);
                payload = await GetAsync(source.CatalogUri, cancellationToken).ConfigureAwait(false);
                payload = DecompressIfGzip(payload);
            }
        }
        else
        {
            payload = await GetAsync(source.CatalogUri, cancellationToken).ConfigureAwait(false);
            payload = DecompressIfGzip(payload);
        }

        var document = JsonSerializer.Deserialize<CatalogDocument>(payload, JsonOptions);
        if (document?.Tracks is null || document.Tracks.Count == 0)
        {
            throw new InvalidDataException("目录内容为空。");
        }

        return document.Tracks;
    }

    private async Task<byte[]> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
    }

    private static bool Verify(RemoteLibraryTrack track, byte[] bytes)
    {
        if (string.IsNullOrEmpty(track.Sha256))
        {
            return true;
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return hash.Equals(track.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecompressIfGzip(byte[] payload)
    {
        if (payload.Length < 2 || payload[0] != 0x1F || payload[1] != 0x8B)
        {
            return payload;
        }

        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private string CachePath => Path.Combine(_cacheDirectory, "online-catalog.json");

    private async Task<RemoteLibraryCatalog?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            var payload = await File.ReadAllBytesAsync(CachePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<CatalogDocument>(payload, JsonOptions);
            if (document?.Tracks is null || document.Tracks.Count == 0)
            {
                return null;
            }

            return new RemoteLibraryCatalog(document.Tracks, FromCache: true, "本地缓存");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 缓存损坏不该影响功能，直接当作没有缓存重新拉取。
            _logger?.LogWarning(exception, "本地曲库目录缓存读取失败");
            return null;
        }
    }

    private async Task WriteCacheAsync(IReadOnlyList<RemoteLibraryTrack> tracks, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new CatalogDocument(1, [.. tracks]), JsonOptions);
            var temporary = CachePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, CachePath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 缓存写不进去只影响下次启动速度，不该让本次加载失败。
            _logger?.LogWarning(exception, "本地曲库目录缓存写入失败");
        }
    }

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private sealed record CatalogDocument(int SchemaVersion, List<RemoteLibraryTrack>? Tracks);
}
