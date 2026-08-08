using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using MeowField.Domain;
using Microsoft.Win32;

namespace MeowField.App;

public partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryService _library;
    private CancellationTokenSource? _scanCancellation;
    private int _offset;

    public LibraryViewModel(ILibraryService library)
    {
        _library = library;
        _ = InitializeAsync();
    }

    public ObservableCollection<LibraryEntry> Entries { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];

    [ObservableProperty] private string query = "";
    [ObservableProperty] private string? selectedFolder;
    [ObservableProperty] private LibraryEntry? selectedEntry;
    [ObservableProperty] private int total;
    [ObservableProperty] private int pageSize = 100;
    [ObservableProperty] private int currentPage = 1;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double scanProgress;
    [ObservableProperty] private string statusText = "正在加载曲库...";

    public bool CanPrevious => _offset > 0 && !IsBusy;
    public bool CanNext => _offset + PageSize < Total && !IsBusy;

    public event EventHandler<string>? LoadRequested;
    public event EventHandler<LibraryEntry>? QueueRequested;

    public async Task InitializeAsync()
    {
        try
        {
            await _library.InitializeAsync();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"曲库加载失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var page = _library.GetPage(_offset, PageSize, SelectedFolder, Query);
        Entries.Clear();
        foreach (var item in page.Entries) Entries.Add(item);
        Folders.Clear();
        foreach (var item in page.Folders) Folders.Add(item);
        Total = page.Total;
        CurrentPage = Total == 0 ? 1 : _offset / PageSize + 1;
        StatusText = Total == 0 ? "曲库为空" : $"共 {Total:N0} 首";
        OnPropertyChanged(nameof(CanPrevious));
        OnPropertyChanged(nameof(CanNext));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "选择 MIDI 曲库目录" };
        if (dialog.ShowDialog() != true) return;
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            ScanProgress = 0;
            var progress = new Progress<ScanProgress>(value =>
            {
                ScanProgress = value.Total == 0 ? 0 : value.Current * 100d / value.Total;
                StatusText = $"正在扫描 {value.Current:N0}/{value.Total:N0}：{value.CurrentName}";
            });
            var added = await _library.ScanFolderAsync(dialog.FolderName, progress, _scanCancellation.Token);
            StatusText = $"扫描完成，新增 {added.Count:N0} 首";
            _offset = 0;
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
        }
        catch (Exception exception)
        {
            StatusText = $"扫描失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    [RelayCommand]
    private async Task AddFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        var added = 0;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                if (await _library.AddAsync(path) is not null) added++;
            }
            catch (Exception exception)
            {
                StatusText = $"添加失败：{exception.Message}";
            }
        }
        StatusText = $"已添加 {added:N0} 首";
        await RefreshAsync();
    }

    [RelayCommand]
    private void LoadSelected()
    {
        if (SelectedEntry is not null) LoadRequested?.Invoke(this, SelectedEntry.Path);
    }

    [RelayCommand]
    private void AddSelectedToQueue()
    {
        if (SelectedEntry is not null) QueueRequested?.Invoke(this, SelectedEntry);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null) return;
        if (MessageBox.Show($"仅从曲库移除“{SelectedEntry.Name}”，不删除原文件？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _library.RemoveAsync(SelectedEntry.Id);
        SelectedEntry = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteSourceAsync()
    {
        if (SelectedEntry is null) return;
        var entry = SelectedEntry;
        var message = $"将永久删除 MIDI 原文件，并从曲库移除：\n\n{entry.Path}\n\n此操作无法在程序中撤销，确定继续？";
        if (MessageBox.Show(message, "删除原文件", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await _library.DeleteSourceAsync(entry.Id);
            SelectedEntry = null;
            StatusText = $"已删除原文件：{entry.Name}";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"删除原文件失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!CanPrevious) return;
        _offset = Math.Max(0, _offset - PageSize);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanNext) return;
        _offset += PageSize;
        await RefreshAsync();
    }

    partial void OnQueryChanged(string value) { _offset = 0; _ = RefreshAsync(); }
    partial void OnSelectedFolderChanged(string? value) { _offset = 0; _ = RefreshAsync(); }
    partial void OnPageSizeChanged(int value) { _offset = 0; _ = RefreshAsync(); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); }
}
