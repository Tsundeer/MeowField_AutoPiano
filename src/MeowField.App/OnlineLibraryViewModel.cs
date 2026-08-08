using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MeowField.App;

public partial class OnlineLibraryViewModel : ObservableObject
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/catalog.json";
    private const string DownloadBaseUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly List<OnlineMidiTrack> _catalog = [];

    public ObservableCollection<OnlineMidiTrack> Tracks { get; } = [];
    public event EventHandler<string>? MidiDownloaded;

    [ObservableProperty] private string query = "";
    [ObservableProperty] private OnlineMidiTrack? selectedTrack;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "加载在线曲库以浏览可下载 MIDI";
    [ObservableProperty] private int total;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "正在获取在线曲库索引...";
            using var response = await _httpClient.GetAsync(CatalogUrl);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var document = await JsonSerializer.DeserializeAsync<OnlineCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _catalog.Clear();
            _catalog.AddRange(document?.Tracks ?? []);
            ApplyFilter();
            StatusText = $"在线曲库已加载：{_catalog.Count:N0} 首";
        }
        catch (Exception exception)
        {
            StatusText = $"在线曲库加载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_catalog.Count == 0)
        {
            await RefreshAsync();
            return;
        }
        ApplyFilter();
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (SelectedTrack is null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在下载：{SelectedTrack.Name}";
            var relativePath = SelectedTrack.Path.Replace('\\', '/');
            var bytes = await _httpClient.GetByteArrayAsync(DownloadBaseUrl + Uri.EscapeDataString(relativePath).Replace("%2F", "/"));
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!hash.Equals(SelectedTrack.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("下载文件校验失败，请稍后重试。");
            }

            var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeowField", "online-library");
            Directory.CreateDirectory(cacheDirectory);
            var targetPath = Path.Combine(cacheDirectory, $"{SelectedTrack.Id}.mid");
            await File.WriteAllBytesAsync(targetPath, bytes);
            MidiDownloaded?.Invoke(this, targetPath);
            StatusText = $"已下载并载入：{SelectedTrack.Name}";
        }
        catch (Exception exception)
        {
            StatusText = $"下载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var keyword = Query.Trim();
        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? _catalog
            : _catalog.Where(track => track.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                                      track.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Tracks.Clear();
        foreach (var track in filtered.Take(500)) Tracks.Add(track);
        Total = filtered.Count;
        SelectedTrack = Tracks.FirstOrDefault();
        StatusText = $"显示 {Tracks.Count:N0}/{Total:N0} 首；可用搜索缩小范围";
    }

    partial void OnQueryChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    private sealed record OnlineCatalog(int SchemaVersion, List<OnlineMidiTrack>? Tracks);
}

public sealed record OnlineMidiTrack(string Id, string Name, string Category, string Path, string Sha256, long Bytes);
