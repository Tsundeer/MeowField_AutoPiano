using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Storage;

public sealed class FileSystemUserDataStore : IUserDataStore
{
    private readonly string _settingsPath;
    private readonly string _presetsPath;
    private readonly string _playlistPath;

    public FileSystemUserDataStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeowField");
        Directory.CreateDirectory(DataDirectory);
        _settingsPath = Path.Combine(DataDirectory, "settings.json");
        _presetsPath = Path.Combine(DataDirectory, "presets.json");
        _playlistPath = Path.Combine(DataDirectory, "playlist.json");
    }

    public string DataDirectory { get; }

    public async Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        await AtomicJsonFile.ReadAsync<UserSettings>(_settingsPath, cancellationToken) ?? new UserSettings();

    public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_settingsPath, settings with { SchemaVersion = UserSettings.CurrentSchemaVersion }, cancellationToken);

    public async Task<IReadOnlyList<Preset>> LoadPresetsAsync(CancellationToken cancellationToken = default) =>
        await AtomicJsonFile.ReadAsync<List<Preset>>(_presetsPath, cancellationToken) ?? [];

    public Task SavePresetsAsync(IReadOnlyList<Preset> presets, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_presetsPath, presets, cancellationToken);

    public async Task<IReadOnlyList<PlaylistItem>> LoadPlaylistAsync(CancellationToken cancellationToken = default) =>
        await AtomicJsonFile.ReadAsync<List<PlaylistItem>>(_playlistPath, cancellationToken) ?? [];

    public Task SavePlaylistAsync(IReadOnlyList<PlaylistItem> playlist, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_playlistPath, playlist, cancellationToken);
}
