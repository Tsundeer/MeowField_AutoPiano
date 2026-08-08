namespace MeowField.Domain;

public sealed record UserSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Locale { get; init; } = "zh-CN";
    public string Theme { get; init; } = "light";
    public string? SelectedGameProfileId { get; init; }
    public string? BoundProcessName { get; init; }
    public string? PianoTransPath { get; init; }
    public MappingConfig Mapping { get; init; } = new();
    public IReadOnlyDictionary<int, string>? PianoKeyMap { get; init; }
    public IReadOnlyDictionary<int, string>? DrumKeyMap { get; init; }
    public IReadOnlyDictionary<int, string>? MicrophoneKeyMap { get; init; }
    public bool AutoPlayNext { get; init; } = true;
}

public sealed record Preset(
    string Id,
    string Name,
    MappingConfig Config,
    DateTimeOffset CreatedAt);

public sealed record PlaylistItem(
    string Id,
    string Path,
    string Name,
    DateTimeOffset AddedAt);

public sealed record GameProfile(
    string Id,
    string Name,
    string? Description,
    MappingConfig Config);

public sealed record LibraryEntry(
    string Id,
    string Path,
    string Name,
    string Folder,
    int DurationMs,
    int Notes,
    DateTimeOffset AddedAt);

public sealed record LibraryPage(
    IReadOnlyList<LibraryEntry> Entries,
    IReadOnlyList<string> Folders,
    int Total,
    int Offset,
    int Limit);

public sealed record ScanProgress(int Current, int Total, int AddedCount, string? CurrentName);

public sealed record ConversionProgress(string Status, string Message, TimeSpan Elapsed);

public sealed record ConversionResult(bool Success, string Message, string? MidiPath);

public sealed record NtpMeasurement(
    bool Success,
    string Server,
    TimeSpan Offset,
    TimeSpan RoundTripTime,
    DateTimeOffset? NetworkTime,
    string? Error = null);

public sealed record ScheduledPlayback(
    string Id,
    DateTimeOffset LocalStart,
    string MidiPath,
    int LinkLatencyMs,
    bool UseNtp,
    bool Enabled = true);

public sealed record LegacyImportResult(
    bool Imported,
    string Message,
    int PresetCount = 0,
    int PlaylistCount = 0);
