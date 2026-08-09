using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Data;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MeowField.App;

public partial class OnlineLibraryViewModel : ObservableObject
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/catalog.json";
    private const string DownloadBaseUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/";
    private const string AllCategoryKey = "*";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly List<OnlineMidiTrack> _catalog = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private List<OnlineMidiTrack> _filtered = [];

    public ObservableCollection<CategoryOption> Categories { get; } = [new(AllCategoryKey, "全部分类")];
    public IReadOnlyList<string> SizeOptions { get; } = ["全部大小", "小于 1 MB", "1–5 MB", "5–10 MB", "大于 10 MB"];
    public IReadOnlyList<string> SortOptions { get; } = ["默认顺序", "名称 A→Z", "名称 Z→A", "大小 小→大", "大小 大→小"];
    public ICollectionView TracksView { get; private set; } = new ListCollectionView(new List<OnlineMidiTrack>());
    public event EventHandler<OnlineMidiPayload>? MidiLoaded;

    [ObservableProperty] private string query = "";
    [ObservableProperty] private string selectedCategoryKey = AllCategoryKey;
    [ObservableProperty] private string sizeFilter = "全部大小";
    [ObservableProperty] private string sortMode = "默认顺序";
    [ObservableProperty] private OnlineMidiTrack? selectedTrack;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "加载在线曲库以浏览可下载 MIDI";
    [ObservableProperty] private int total;

    public async Task EnsureLoadedAsync()
    {
        if (_catalog.Count > 0) return;
        await _refreshGate.WaitAsync();
        try
        {
            if (_catalog.Count > 0) return;
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync()
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
            RebuildCategories();
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
            await EnsureLoadedAsync();
            return;
        }
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadSelectedAsync()
    {
        if (SelectedTrack is null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在加载：{SelectedTrack.Name}";
            var relativePath = SelectedTrack.Path.Replace('\\', '/');
            var bytes = await _httpClient.GetByteArrayAsync(DownloadBaseUrl + Uri.EscapeDataString(relativePath).Replace("%2F", "/"));
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!hash.Equals(SelectedTrack.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("下载文件校验失败，请稍后重试。");
            }

            MidiLoaded?.Invoke(this, new OnlineMidiPayload(SelectedTrack.Name, bytes));
            StatusText = $"已加载播放：{SelectedTrack.Name}";
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
        IEnumerable<OnlineMidiTrack> filtered = _catalog;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(track =>
                track.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                track.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                track.DisplayCategory.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        }

        if (SelectedCategoryKey != AllCategoryKey)
        {
            filtered = filtered.Where(track => string.Equals(track.Category, SelectedCategoryKey, StringComparison.OrdinalIgnoreCase));
        }

        filtered = ApplySizeFilter(filtered);
        var list = filtered.ToList();
        ApplySort(list);

        _filtered = list;
        TracksView = new ListCollectionView(_filtered);
        OnPropertyChanged(nameof(TracksView));
        Total = _filtered.Count;
        SelectedTrack = _filtered.FirstOrDefault();
        StatusText = $"显示 {Total:N0} 首；可用搜索、分类、大小和排序缩小范围";
    }

    private IEnumerable<OnlineMidiTrack> ApplySizeFilter(IEnumerable<OnlineMidiTrack> source)
    {
        const long mb = 1024 * 1024;
        return SizeFilter switch
        {
            "小于 1 MB" => source.Where(track => track.Bytes < mb),
            "1–5 MB" => source.Where(track => track.Bytes >= mb && track.Bytes < 5 * mb),
            "5–10 MB" => source.Where(track => track.Bytes >= 5 * mb && track.Bytes < 10 * mb),
            "大于 10 MB" => source.Where(track => track.Bytes >= 10 * mb),
            _ => source,
        };
    }

    private void ApplySort(List<OnlineMidiTrack> list)
    {
        switch (SortMode)
        {
            case "名称 A→Z":
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                break;
            case "名称 Z→A":
                list.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.CurrentCultureIgnoreCase));
                break;
            case "大小 小→大":
                list.Sort((a, b) => a.Bytes.CompareTo(b.Bytes));
                break;
            case "大小 大→小":
                list.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
                break;
        }
    }

    private void RebuildCategories()
    {
        var previous = SelectedCategoryKey;
        Categories.Clear();
        Categories.Add(new CategoryOption(AllCategoryKey, "全部分类"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in _catalog)
        {
            if (seen.Add(track.Category))
            {
                Categories.Add(new CategoryOption(track.Category, track.DisplayCategory));
            }
        }

        if (!Categories.Any(option => option.Key == previous))
        {
            SelectedCategoryKey = AllCategoryKey;
        }
    }

    partial void OnQueryChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnSelectedCategoryKeyChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnSizeFilterChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnSortModeChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    private sealed record OnlineCatalog(int SchemaVersion, List<OnlineMidiTrack>? Tracks);
}

public sealed record CategoryOption(string Key, string Display);

public sealed record OnlineMidiTrack(string Id, string Name, string Category, string Path, string Sha256, long Bytes)
{
    public string DisplayCategory => Category switch
    {
        "game-music" => "游戏音乐",
        "anime-acg" => "动漫与 ACG",
        "virtual-singers" => "虚拟歌手",
        "classical-light" => "古典与轻音乐",
        "drums-rhythm" => "鼓组与节奏",
        "asian-pop" => "华语与亚洲流行",
        "world-pop" => "欧美与其他",
        _ => "未分类",
    };
}

public sealed record OnlineMidiPayload(string Name, byte[] Content);
