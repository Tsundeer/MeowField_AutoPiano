using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Data;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using Microsoft.Win32;

namespace MeowField.App;

public partial class OnlineLibraryViewModel : ObservableObject
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/catalog.json";
    private const string DownloadBaseUrl = "https://raw.githubusercontent.com/Tsundeer/MeowField_MidiLibrary/main/";
    private const string AllCategoryKey = "*";
    private readonly IUserDataStore _store;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly List<OnlineMidiTrack> _catalog = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly HashSet<string> _favoriteIds = new(StringComparer.Ordinal);
    private List<OnlineMidiTrack> _filtered = [];
    private bool _favoritesLoaded;

    public OnlineLibraryViewModel(IUserDataStore store) => _store = store;

    public ObservableCollection<CategoryOption> Categories { get; } = [new(AllCategoryKey, "全部分类")];
    public IReadOnlyList<string> SizeOptions { get; } = ["全部大小", "小于 1 MB", "1–5 MB", "5–10 MB", "大于 10 MB"];
    public IReadOnlyList<string> SortOptions { get; } = ["默认顺序", "名称 A→Z", "名称 Z→A", "大小 小→大", "大小 大→小"];
    public ICollectionView TracksView { get; private set; } = new ListCollectionView(new List<OnlineMidiTrack>());
    public event EventHandler<OnlineMidiPayload>? MidiLoaded;
    public event EventHandler<string>? DownloadSaved;

    [ObservableProperty] private string query = "";
    // ItemsSource 清空重建时 ComboBox 会把 SelectedValue 写回 null，因此允许瞬态 null。
    [ObservableProperty] private string? selectedCategoryKey = AllCategoryKey;
    [ObservableProperty] private string sizeFilter = "全部大小";
    [ObservableProperty] private string sortMode = "默认顺序";
    [ObservableProperty] private OnlineMidiTrack? selectedTrack;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "尚未加载";
    [ObservableProperty] private int total;
    [ObservableProperty] private bool onlyFavorites;

    public async Task EnsureLoadedAsync()
    {
        if (_catalog.Count > 0) return;
        await _refreshGate.WaitAsync();
        try
        {
            if (_catalog.Count > 0) return;
            if (!_favoritesLoaded)
            {
                _favoriteIds.UnionWith(await _store.LoadFavoriteTracksAsync());
                _favoritesLoaded = true;
            }

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
            if (!_favoritesLoaded)
            {
                _favoriteIds.UnionWith(await _store.LoadFavoriteTracksAsync());
                _favoritesLoaded = true;
            }

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
            StatusText = "加载中...";
            using var response = await _httpClient.GetAsync(CatalogUrl);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var document = await JsonSerializer.DeserializeAsync<OnlineCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _catalog.Clear();
            _catalog.AddRange(document?.Tracks ?? []);
            RebuildCategories();
            ApplyFilter();
            StatusText = $"已加载 {_catalog.Count:N0} 首";
        }
        catch (Exception exception)
        {
            StatusText = $"加载失败：{exception.Message}";
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
        await DownloadAndUseAsync(SelectedTrack, saveTo: null);
    }

    [RelayCommand]
    private async Task DownloadTrackAsync(OnlineMidiTrack? track)
    {
        if (track is null || IsBusy) return;
        var dialog = new SaveFileDialog
        {
            FileName = $"{SanitizeFileName(track.Name)}.mid",
            Filter = "MIDI 文件 (*.mid)|*.mid|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        await DownloadAndUseAsync(track, dialog.FileName);
    }

    private async Task DownloadAndUseAsync(OnlineMidiTrack track, string? saveTo)
    {
        try
        {
            IsBusy = true;
            StatusText = $"下载中 {track.Name}";
            var bytes = await DownloadTrackBytesAsync(track);
            if (saveTo is null)
            {
                MidiLoaded?.Invoke(this, new OnlineMidiPayload(track.Name, bytes, track.Id));
                StatusText = $"已载入 {track.Name}";
                return;
            }

            await File.WriteAllBytesAsync(saveTo, bytes);
            StatusText = $"已下载 {track.Name}";
            DownloadSaved?.Invoke(this, saveTo);
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

    private async Task<byte[]> DownloadTrackBytesAsync(OnlineMidiTrack track)
    {
        var relativePath = track.Path.Replace('\\', '/');
        var bytes = await _httpClient.GetByteArrayAsync(DownloadBaseUrl + Uri.EscapeDataString(relativePath).Replace("%2F", "/"));
        if (!string.IsNullOrEmpty(track.Sha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!hash.Equals(track.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("文件校验失败，请稍后重试。");
            }
        }

        return bytes;
    }

    [RelayCommand]
    private void ToggleFavorite(OnlineMidiTrack? track)
    {
        if (track is null) return;
        track.IsFavorite = !track.IsFavorite;
        var changed = track.IsFavorite ? _favoriteIds.Add(track.Id) : _favoriteIds.Remove(track.Id);
        if (changed) _ = _store.SaveFavoriteTracksAsync(_favoriteIds.ToList());
        if (OnlyFavorites && _catalog.Count > 0) ApplyFilter();
    }

    private static string SanitizeFileName(string name)
    {
        var clean = new string(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "track" : clean;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanModify));

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

        if (!string.IsNullOrEmpty(SelectedCategoryKey) && SelectedCategoryKey != AllCategoryKey)
        {
            filtered = filtered.Where(track => string.Equals(track.Category, SelectedCategoryKey, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var track in filtered) track.IsFavorite = _favoriteIds.Contains(track.Id);
        filtered = ApplySizeFilter(filtered);
        if (OnlyFavorites) filtered = filtered.Where(track => track.IsFavorite);
        var list = filtered.ToList();
        ApplySort(list);

        _filtered = list;
        TracksView = new ListCollectionView(_filtered);
        OnPropertyChanged(nameof(TracksView));
        Total = _filtered.Count;
        SelectedTrack = _filtered.FirstOrDefault();
        StatusText = $"共 {Total:N0} 首";
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

        // Clear 会把 SelectedValue 写回 null，必须无条件恢复选中项。
        SelectedCategoryKey = Categories.Any(option => option.Key == previous) ? previous : AllCategoryKey;
    }

    partial void OnQueryChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnSelectedCategoryKeyChanged(string? value)
    {
        if (_catalog.Count > 0 && value is not null) ApplyFilter();
    }

    partial void OnSizeFilterChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnSortModeChanged(string value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    partial void OnOnlyFavoritesChanged(bool value)
    {
        if (_catalog.Count > 0) ApplyFilter();
    }

    public bool CanModify => !IsBusy;

    private sealed record OnlineCatalog(int SchemaVersion, List<OnlineMidiTrack>? Tracks);
}

public sealed record CategoryOption(string Key, string Display);

public sealed partial class OnlineMidiTrack : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Bytes { get; set; }

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

    public string SizeLabel => Bytes switch
    {
        >= 1024 * 1024 => $"{Bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{Bytes / 1024d:0.#} KB",
        _ => $"{Bytes} B",
    };

    [ObservableProperty] private bool isFavorite;
}

public sealed record OnlineMidiPayload(string Name, byte[] Content, string CacheKey);
