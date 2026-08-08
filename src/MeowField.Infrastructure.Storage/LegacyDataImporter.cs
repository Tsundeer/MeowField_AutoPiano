using System.Text;
using System.Text.Json;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Storage;

public sealed class LegacyDataImporter : ILegacyDataImporter
{
    private const string ConfigKey = "devspace-autoplayer-config";
    private const string PresetsKey = "devspace-autoplayer-presets";
    private const string PlaylistKey = "devspace-autoplayer-playlist";
    private readonly IUserDataStore _store;
    private readonly string _legacyLevelDb;

    public LegacyDataImporter(
        IUserDataStore store,
        string? legacyRoamingDirectory = null)
    {
        _store = store;
        var roaming = legacyRoamingDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "meowfield-autoplayer-lite");
        _legacyLevelDb = Path.Combine(roaming, "Local Storage", "leveldb");
    }

    public async Task<LegacyImportResult> ImportIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var marker = Path.Combine(_store.DataDirectory, "legacy-migration.json");
        if (File.Exists(marker) || File.Exists(Path.Combine(_store.DataDirectory, "settings.json")))
        {
            return new LegacyImportResult(false, "无需迁移");
        }

        if (!Directory.Exists(_legacyLevelDb))
        {
            return new LegacyImportResult(false, "未发现旧版数据");
        }

        var sourceFiles = Directory.EnumerateFiles(_legacyLevelDb)
            .Where(path => Path.GetExtension(path) is ".log" or ".ldb")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            return new LegacyImportResult(false, "旧版数据目录为空");
        }

        var payload = new StringBuilder();
        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload.Append(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, cancellationToken)));
        }

        var configJson = ExtractLatest(payload.ToString(), ConfigKey, '{', '}');
        var presetsJson = ExtractLatest(payload.ToString(), PresetsKey, '[', ']');
        var playlistJson = ExtractLatest(payload.ToString(), PlaylistKey, '[', ']');
        if (configJson is null && presetsJson is null && playlistJson is null)
        {
            return new LegacyImportResult(false, "未找到可识别的旧版配置");
        }

        var backupDirectory = Path.Combine(_store.DataDirectory, "migration-backups", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupDirectory);
        foreach (var file in sourceFiles)
        {
            File.Copy(file, Path.Combine(backupDirectory, Path.GetFileName(file)), overwrite: false);
        }

        var settings = configJson is null ? new UserSettings() : ParseSettings(configJson);
        var presets = presetsJson is null ? [] : ParsePresets(presetsJson);
        var playlist = playlistJson is null ? [] : ParsePlaylist(playlistJson);
        await _store.SaveSettingsAsync(settings, cancellationToken);
        await _store.SavePresetsAsync(presets, cancellationToken);
        await _store.SavePlaylistAsync(playlist, cancellationToken);

        var result = new LegacyImportResult(true, "旧版配置已迁移", presets.Count, playlist.Count);
        await AtomicJsonFile.WriteAsync(marker, result, cancellationToken);
        return result;
    }

    private static UserSettings ParseSettings(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var instrument = ParseInstrument(GetString(root, "instrument"));
        var mapping = new MappingConfig
        {
            Instrument = instrument,
            InputMode = GetString(root, "inputMode") == "message" ? InputMode.WindowMessage : InputMode.SendInput,
            MidiChannelFilter = GetNullableInt(root, "midiChannelFilter"),
            NoteRangeLow = GetInt(root, "noteRangeLow", instrument == InstrumentKind.Microphone ? 60 : 48),
            NoteRangeHigh = GetInt(root, "noteRangeHigh", instrument == InstrumentKind.Microphone ? 74 : 83),
            PreferNearestWhite = GetBool(root, "preferNearestWhite", true),
            TransposeSemitones = GetInt(root, "transpose", 0),
            Speed = GetDouble(root, "speed", 1),
            MaxPolyphony = GetInt(root, "maxPolyphony", 10),
            ChordMode = ParseChordMode(GetString(root, "chordMode")),
            AutoTranspose = GetBool(root, "autoTranspose", true),
            LinkLatencyMs = GetInt(root, "linkLatencyMs", 0),
            CustomKeyMap = GetIntStringDictionary(root, "customKeyMap"),
        };

        return new UserSettings
        {
            Locale = GetString(root, "locale") ?? "zh-CN",
            SelectedGameProfileId = GetString(root, "selectedGameId"),
            BoundProcessName = GetString(root, "bindProcess"),
            PianoTransPath = GetString(root, "pianoTransPath"),
            Mapping = mapping,
            PianoKeyMap = GetIntStringDictionary(root, "pianoKeyMap"),
            DrumKeyMap = GetIntStringDictionary(root, "drumKeyMap"),
            MicrophoneKeyMap = GetIntStringDictionary(root, "microphoneKeyMap"),
        };
    }

    private static IReadOnlyList<Preset> ParsePresets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<Preset>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("config", out var config)) continue;
            var settings = ParseSettings(config.GetRawText());
            var id = GetString(item, "id") ?? Guid.NewGuid().ToString("N");
            var name = GetString(item, "name") ?? "旧版预设";
            var created = item.TryGetProperty("createdAt", out var value) && value.TryGetInt64(out var milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                : DateTimeOffset.Now;
            result.Add(new Preset(id, name, settings.Mapping, created));
        }
        return result;
    }

    private static IReadOnlyList<PlaylistItem> ParsePlaylist(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<PlaylistItem>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var path = GetString(item, "path");
            if (string.IsNullOrWhiteSpace(path)) continue;
            var id = GetString(item, "id") ?? Guid.NewGuid().ToString("N");
            var name = GetString(item, "name") ?? Path.GetFileNameWithoutExtension(path);
            var added = item.TryGetProperty("addedAt", out var value) && value.TryGetInt64(out var milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                : DateTimeOffset.Now;
            result.Add(new PlaylistItem(id, path, name, added));
        }
        return result;
    }

    private static string? ExtractLatest(string input, string key, char open, char close)
    {
        string? latest = null;
        var searchAt = 0;
        while ((searchAt = input.IndexOf(key, searchAt, StringComparison.Ordinal)) >= 0)
        {
            var start = input.IndexOf(open, searchAt + key.Length);
            if (start < 0) break;
            var end = FindBalancedEnd(input, start, open, close);
            if (end < 0) { searchAt += key.Length; continue; }
            var candidate = input[start..(end + 1)];
            try
            {
                using var parsed = JsonDocument.Parse(candidate);
                latest = candidate;
            }
            catch (JsonException)
            {
                // Binary LevelDB framing can leave false candidates; continue to the next record.
            }
            searchAt = end + 1;
        }
        return latest;
    }

    private static int FindBalancedEnd(string input, int start, char open, char close)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = start; index < input.Length; index++)
        {
            var current = input[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') quoted = false;
                continue;
            }
            if (current == '"') quoted = true;
            else if (current == open) depth++;
            else if (current == close && --depth == 0) return index;
        }
        return -1;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int GetInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static int? GetNullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static double GetDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    private static bool GetBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static IReadOnlyDictionary<int, string>? GetIntStringDictionary(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Where(item => int.TryParse(item.Name, out _) && item.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(item => int.Parse(item.Name), item => item.Value.GetString()!)
            : null;
    private static InstrumentKind ParseInstrument(string? value) => value switch
    {
        "drums" => InstrumentKind.Drums,
        "microphone" => InstrumentKind.Microphone,
        _ => InstrumentKind.Piano,
    };
    private static ChordMode ParseChordMode(string? value) => value switch
    {
        "off" => ChordMode.Off,
        "melody" => ChordMode.Melody,
        "smart" => ChordMode.Smart,
        _ => ChordMode.Prefer,
    };
}
