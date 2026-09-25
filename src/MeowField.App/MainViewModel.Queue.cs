using System.IO;
using MeowField.Domain;

namespace MeowField.App;

public partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PlayQueueItemAsync(PlaylistItem? item)
    {
        if (item is null) return;
        SelectedQueueItem = item;
        await LoadMidiAsync(item.Path, addToQueue: false, displayName: item.Name);
        await PlayPause();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RemoveQueueItemAsync(PlaylistItem? item)
    {
        if (item is null) return;
        var index = Queue.IndexOf(item);
        Queue.Remove(item);
        if (ReferenceEquals(SelectedQueueItem, item))
        {
            SelectedQueueItem = Queue.Count == 0 ? null : Queue[Math.Clamp(index, 0, Queue.Count - 1)];
        }

        await _store.SavePlaylistAsync(Queue);
        RaiseQueueState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task MoveQueueItemUpAsync(PlaylistItem? item) => await MoveQueueItemAsync(item, -1);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task MoveQueueItemDownAsync(PlaylistItem? item) => await MoveQueueItemAsync(item, 1);

    private async Task MoveQueueItemAsync(PlaylistItem? item, int offset)
    {
        if (item is null) return;
        var index = Queue.IndexOf(item);
        var next = index + offset;
        if (index < 0 || next < 0 || next >= Queue.Count) return;
        Queue.Move(index, next);
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
    private async Task PlayNextAsync() => await PlayQueueOffsetAsync(1);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PlayPreviousAsync() => await PlayQueueOffsetAsync(-1);

    private async Task PlayQueueOffsetAsync(int offset)
    {
        if (Queue.Count == 0) return;
        var index = SelectedQueueItem is null ? -1 : Queue.IndexOf(SelectedQueueItem);
        var next = ResolveQueueIndex(index, offset);
        if (next < 0 || next >= Queue.Count) return;
        var item = Queue[next];
        SelectedQueueItem = item;
        await LoadMidiAsync(item.Path, addToQueue: false, displayName: item.Name);
        await PlayPause();
    }

    private int ResolveQueueIndex(int index, int offset)
    {
        return PlaybackMode switch
        {
            PlaybackMode.Shuffle when offset > 0 => PickRandomQueueIndex(index),
            PlaybackMode.LoopAll or PlaybackMode.RepeatOne => (index + offset + Queue.Count) % Queue.Count,
            _ => index + offset,
        };
    }

    private int PickRandomQueueIndex(int currentIndex)
    {
        if (Queue.Count <= 1) return 0;
        var next = Random.Shared.Next(Queue.Count - 1);
        if (next >= currentIndex) next++;
        return next;
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
    }
}
