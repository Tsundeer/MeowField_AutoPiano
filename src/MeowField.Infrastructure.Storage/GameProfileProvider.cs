using System.Text.Json;
using System.Text.Json.Serialization;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Storage;

public sealed class GameProfileProvider(string profilesDirectory) : IGameProfileProvider
{
    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(profilesDirectory))
        {
            return [];
        }

        var profiles = new List<GameProfile>();
        foreach (var path in Directory.EnumerateFiles(profilesDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<ProfileDto>(stream, JsonDefaults.Options, cancellationToken);
            if (data is null || string.IsNullOrWhiteSpace(data.Id) || string.IsNullOrWhiteSpace(data.Name))
            {
                continue;
            }

            var config = new MappingConfig
            {
                Instrument = ParseInstrument(data.Instrument),
                MidiChannelFilter = data.MidiChannelFilter,
                NoteRangeLow = data.NoteRangeLow ?? 48,
                NoteRangeHigh = data.NoteRangeHigh ?? 83,
                PreferNearestWhite = data.PreferNearestWhite ?? true,
                NearestWhiteDirection = ParseNearestWhiteDirection(data.NearestWhiteDirection),
                TransposeSemitones = data.TransposeSemitones,
                Speed = data.Speed,
                MaxPolyphony = data.MaxPolyphony,
                ChordMode = ParseChordMode(data.ChordMode),
                KeepMelodyTopNote = data.KeepMelodyTopNote,
                ChordClusterWindowMs = data.ChordClusterWindowMs,
                AutoTranspose = data.AutoTranspose,
                LinkLatencyMs = data.LinkLatencyMs,
                CustomKeyMap = data.CustomKeyMap,
            };
            config.Validate();
            profiles.Add(new GameProfile(data.Id, data.Name, data.Description, config));
        }

        return profiles;
    }

    private static InstrumentKind ParseInstrument(string? value) => value?.ToLowerInvariant() switch
    {
        "drums" => InstrumentKind.Drums,
        "microphone" => InstrumentKind.Microphone,
        _ => InstrumentKind.Piano,
    };

    private static ChordMode ParseChordMode(string? value) => value?.ToLowerInvariant() switch
    {
        "off" => ChordMode.Off,
        "melody" => ChordMode.Melody,
        "smart" => ChordMode.Smart,
        _ => ChordMode.Prefer,
    };

    private static NearestWhiteDirection ParseNearestWhiteDirection(string? value) => value?.ToLowerInvariant() switch
    {
        "up" => NearestWhiteDirection.Up,
        _ => NearestWhiteDirection.Down,
    };

    private sealed record ProfileDto
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public string? Instrument { get; init; }
        [JsonPropertyName("midi_channel_filter")] public int? MidiChannelFilter { get; init; }
        [JsonPropertyName("note_range_low")] public int? NoteRangeLow { get; init; }
        [JsonPropertyName("note_range_high")] public int? NoteRangeHigh { get; init; }
        [JsonPropertyName("prefer_nearest_white")] public bool? PreferNearestWhite { get; init; }
        [JsonPropertyName("nearest_white_direction")] public string? NearestWhiteDirection { get; init; }
        [JsonPropertyName("transpose_semitones")] public int TransposeSemitones { get; init; }
        public double Speed { get; init; } = 1;
        [JsonPropertyName("max_polyphony")] public int MaxPolyphony { get; init; } = 10;
        [JsonPropertyName("chord_mode")] public string? ChordMode { get; init; }
        [JsonPropertyName("keep_melody_top_note")] public bool KeepMelodyTopNote { get; init; } = true;
        [JsonPropertyName("chord_cluster_window_ms")] public int ChordClusterWindowMs { get; init; } = 40;
        [JsonPropertyName("auto_transpose")] public bool AutoTranspose { get; init; }
        [JsonPropertyName("link_latency_ms")] public int LinkLatencyMs { get; init; }
        [JsonPropertyName("custom_key_map")] public Dictionary<int, string>? CustomKeyMap { get; init; }
    }
}
