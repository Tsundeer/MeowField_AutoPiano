using System.IO;
using MeowField.Domain;

namespace MeowField.App;

public partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RemoveSelectedQueueAsync()
    {
        if (SelectedQueueItem is null) return;
        Queue.Remove(SelectedQueueItem);
        SelectedQueueItem = Queue.FirstOrDefault();
        await _store.SavePlaylistAsync(Queue);
        RaiseQueueState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ClearQueueAsync()
    {
        Queue.Clear();
        SelectedQueueItem = null;
        await _store.SavePlaylistAsync(Queue);
        RaiseQueueState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task MoveQueueUpAsync() => await MoveQueueAsync(-1);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task MoveQueueDownAsync() => await MoveQueueAsync(1);

    private async Task MoveQueueAsync(int offset)
    {
        if (SelectedQueueItem is null) return;
        var index = Queue.IndexOf(SelectedQueueItem);
        var next = index + offset;
        if (index < 0 || next < 0 || next >= Queue.Count) return;
        Queue.Move(index, next);
        await _store.SavePlaylistAsync(Queue);
        RaiseQueueState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PlayNextAsync() => await PlayQueueOffsetAsync(1);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PlayPreviousAsync() => await PlayQueueOffsetAsync(-1);

    private async Task PlayQueueOffsetAsync(int offset)
    {
        if (Queue.Count == 0) return;
        var index = SelectedQueueItem is null ? -1 : Queue.IndexOf(SelectedQueueItem);
        var next = index + offset;
        if (next < 0 || next >= Queue.Count) return;
        SelectedQueueItem = Queue[next];
        await LoadMidiAsync(SelectedQueueItem.Path);
        await PlayPause();
    }

    private void AddToQueue(LibraryEntry entry)
    {
        EnsureQueueItem(entry.Path, entry.Name, entry.Id);
    }

    private bool EnsureQueueItem(string path, string? name = null, string? id = null)
    {
        var normalizedPath = Path.GetFullPath(path);
        var existing = Queue.FirstOrDefault(item => string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedQueueItem = existing;
            return false;
        }

        var item = new PlaylistItem(id ?? Guid.NewGuid().ToString("N"), normalizedPath,
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(normalizedPath) : name,
            DateTimeOffset.UtcNow);
        Queue.Add(item);
        SelectedQueueItem = item;
        RaiseQueueState();
        _ = _store.SavePlaylistAsync(Queue);
        return true;
    }

    private void RaiseQueueState()
    {
        OnPropertyChanged(nameof(CanPlayPrevious));
        OnPropertyChanged(nameof(CanPlayNext));
        OnPropertyChanged(nameof(CanMoveQueueUp));
        OnPropertyChanged(nameof(CanMoveQueueDown));
    }
}
