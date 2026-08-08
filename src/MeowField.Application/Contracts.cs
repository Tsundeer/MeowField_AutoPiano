using MeowField.Domain;

namespace MeowField.Application;

public interface IMidiFileReader
{
    Task<ParsedMidi> ReadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed record WindowTarget(nint Handle, int ProcessId, string ProcessName, string Title)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{ProcessName} - {Title}";
}

public interface IWindowCatalog
{
    IReadOnlyList<WindowTarget> ListVisibleWindows();
    bool IsWindow(nint handle);
    bool TryActivate(nint handle);
    bool IsForeground(nint handle);
    bool IsAdministrator { get; }
}

public interface IInputSink
{
    void SendBatch(IReadOnlyList<PlayEvent> events, InputMode mode, nint targetWindow, int latencyMs);
    void ReleaseAll(IEnumerable<string> keys, InputMode mode, nint targetWindow);
}

public enum PlaybackState
{
    Idle,
    Loaded,
    Playing,
    Paused,
    Stopped,
    Faulted,
}

public sealed record PlaybackSnapshot(
    PlaybackState State,
    int CursorMs,
    int DurationMs,
    IReadOnlySet<string> ActiveKeys,
    string? Error = null)
{
    public double Progress => DurationMs <= 0 ? 0 : Math.Clamp(CursorMs * 100d / DurationMs, 0, 100);
}

public interface IPlaybackEngine : IDisposable
{
    event EventHandler<PlaybackSnapshot>? SnapshotChanged;
    PlaybackSnapshot Snapshot { get; }
    void Load(ParsedMidi midi, MappingConfig config);
    void Play(nint targetWindow);
    void Pause();
    void Stop();
}

public interface IUserDataStore
{
    string DataDirectory { get; }
    Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Preset>> LoadPresetsAsync(CancellationToken cancellationToken = default);
    Task SavePresetsAsync(IReadOnlyList<Preset> presets, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistItem>> LoadPlaylistAsync(CancellationToken cancellationToken = default);
    Task SavePlaylistAsync(IReadOnlyList<PlaylistItem> playlist, CancellationToken cancellationToken = default);
}

public interface IGameProfileProvider
{
    Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken cancellationToken = default);
}

public interface ILibraryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    LibraryPage GetPage(int offset, int limit, string? folder = null, string? query = null);
    Task<IReadOnlyList<LibraryEntry>> ScanFolderAsync(
        string path,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<LibraryEntry?> AddAsync(string path, string? folder = null, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteSourceAsync(string id, CancellationToken cancellationToken = default);
    Task<int> RemoveFolderAsync(string folder, CancellationToken cancellationToken = default);
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}

public interface IAudioConverter
{
    string? ExecutablePath { get; }
    bool IsAvailable { get; }
    IReadOnlyList<string> SupportedExtensions { get; }
    Task<(bool Success, string Message)> SetPathAsync(string path, CancellationToken cancellationToken = default);
    Task<ConversionResult> ConvertAsync(
        string audioPath,
        string? outputPath = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface INtpService
{
    Task<NtpMeasurement> MeasureAsync(CancellationToken cancellationToken = default);
}

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? PlayPauseRequested;
    event EventHandler? StopRequested;
    bool IsEnabled { get; }
    bool TryRegister(nint windowHandle);
    void Unregister();
    bool HandleWindowMessage(int message, nint wParam);
}

public interface IDiagnosticService
{
    string LogDirectory { get; }
    Task ExportAsync(string outputPath, CancellationToken cancellationToken = default);
}

public interface IScheduledPlaybackService : IDisposable
{
    event EventHandler<ScheduledPlayback>? Due;
    ScheduledPlayback? Current { get; }
    Task ScheduleAsync(ScheduledPlayback schedule, NtpMeasurement? measurement = null, CancellationToken cancellationToken = default);
    void Cancel();
}

public interface ILegacyDataImporter
{
    Task<LegacyImportResult> ImportIfNeededAsync(CancellationToken cancellationToken = default);
}
