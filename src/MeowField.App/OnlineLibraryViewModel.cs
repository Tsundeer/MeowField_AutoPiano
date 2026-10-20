using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using Microsoft.Win32;

namespace MeowField.App;

public partial class OnlineLibraryViewModel : ObservableObject
{
    private const string AllCategoryKey = "*";

    // 目录有四个分类合计近两万条，逐字符全量过滤会明显掉帧，输入停顿后再算。
    private const int FilterDelayMs = 220;

    private readonly IUserDataStore _store;
    private readonly IRemoteLibraryClient _client;
    private readonly List<OnlineMidiTrack> _catalog = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly HashSet<string> _favoriteIds = new(StringComparer.Ordinal);
    private List<OnlineMidiTrack> _filtered = [];
    private bool _favoritesLoaded;
    private int _filterRequest;

    public OnlineLibraryViewModel(IRemoteLibraryClient client, IUserDataStore store)
    {
        _client = client;
        _store = store;
    }

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
            await LoadAsync(forceRefresh: false);
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
            await LoadAsync(forceRefresh: true);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        try
        {
            IsBusy = true;
            StatusText = forceRefresh ? "正在刷新曲库…" : "正在加载曲库…";
            await EnsureFavoritesLoadedAsync();

            var catalog = await _client.LoadCatalogAsync(forceRefresh);
            _catalog.Clear();
            _catalog.AddRange(catalog.Tracks.Select(OnlineMidiTrack.FromRemote));
            RebuildCategories();
            ApplyFilter();

            StatusText = catalog.FromCache
                ? $"已加载 {_catalog.Count:N0} 首（离线缓存，点「刷新曲库」可重试）"
                : $"已加载 {_catalog.Count:N0} 首 · 来源 {catalog.SourceName}";
        }
        catch (RemoteLibraryException exception)
        {
            StatusText = exception.UserMessage;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = "曲库加载失败，请检查网络后点「刷新曲库」重试。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnsureFavoritesLoadedAsync()
    {
        if (_favoritesLoaded) return;
        _favoriteIds.UnionWith(await _store.LoadFavoriteTracksAsync());
        _favoritesLoaded = true;
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
            var bytes = await _client.DownloadTrackAsync(track.ToRemoteTrack());
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
        catch (RemoteLibraryException exception)
        {
            StatusText = exception.UserMessage;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"「{track.Name}」保存失败，请重试。";
        }
        finally
        {
            IsBusy = false;
        }
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
        _filterRequest++;
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

        // 只在真的变化时赋值，避免上万次无意义的 PropertyChanged。
        foreach (var track in filtered)
        {
            var favorite = _favoriteIds.Contains(track.Id);
            if (track.IsFavorite != favorite) track.IsFavorite = favorite;
        }

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

    partial void OnQueryChanged(string value) => RequestFilter();

    partial void OnSelectedCategoryKeyChanged(string? value) => RequestFilter(immediate: true);

    partial void OnSizeFilterChanged(string value) => RequestFilter(immediate: true);

    partial void OnSortModeChanged(string value) => RequestFilter(immediate: true);

    partial void OnOnlyFavoritesChanged(bool value) => RequestFilter(immediate: true);

    public bool CanModify => !IsBusy;

    /// <summary>
    /// 输入框逐字符触发，用延迟合并；下拉框选择是离散动作，直接生效。
    /// </summary>
    private void RequestFilter(bool immediate = false)
    {
        if (_catalog.Count == 0) return;
        var token = ++_filterRequest;
        if (immediate)
        {
            ApplyFilter();
            return;
        }

        _ = FilterAfterDelayAsync(token);
    }

    private async Task FilterAfterDelayAsync(int token)
    {
        try
        {
            // 不写 ConfigureAwait(false)：续体需要回到 UI 线程再改集合。
            await Task.Delay(FilterDelayMs);
            if (token == _filterRequest) ApplyFilter();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }
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

    public static OnlineMidiTrack FromRemote(RemoteLibraryTrack track) => new()
    {
        Id = track.Id,
        Name = track.Name,
        Category = track.Category,
        Path = track.Path,
        Sha256 = track.Sha256,
        Bytes = track.Bytes,
    };

    public RemoteLibraryTrack ToRemoteTrack() => new(Id, Name, Category, Path, Sha256, Bytes);

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
