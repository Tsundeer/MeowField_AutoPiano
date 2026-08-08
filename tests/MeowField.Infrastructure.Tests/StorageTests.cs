using MeowField.Domain;
using MeowField.Infrastructure.Midi;
using MeowField.Infrastructure.Storage;
using System.IO.Compression;

namespace MeowField.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meowfield-tests-{Guid.NewGuid():N}");

    public StorageTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task UserDataStore_RoundTripsVersionedDocumentsAndCreatesBackup()
    {
        var store = new FileSystemUserDataStore(_directory);
        var first = new UserSettings { Locale = "en-US", Mapping = new MappingConfig { Speed = 1.25 } };
        await store.SaveSettingsAsync(first);
        await store.SaveSettingsAsync(first with { Theme = "dark" });

        var loaded = await store.LoadSettingsAsync();

        Assert.Equal(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("en-US", loaded.Locale);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal(1.25, loaded.Mapping.Speed);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json.bak")));
    }

    [Fact]
    public async Task UserDataStore_RoundTripsLocaleThemeInstrumentRangesAndAllKeyMaps()
    {
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "settings-rich"));
        var settings = new UserSettings
        {
            Locale = "zh-CN",
            Theme = "dark",
            AutoPlayNext = false,
            SelectedGameProfileId = "profile-1",
            BoundProcessName = "game.exe",
            Mapping = new MappingConfig
            {
                Instrument = InstrumentKind.Microphone,
                NoteRangeLow = 60,
                NoteRangeHigh = 74,
                InputMode = InputMode.WindowMessage,
                LinkLatencyMs = -120,
                CustomKeyMap = new Dictionary<int, string> { [60] = "A" },
            },
            PianoKeyMap = new Dictionary<int, string> { [60] = "Q" },
            DrumKeyMap = new Dictionary<int, string> { [36] = "E" },
            MicrophoneKeyMap = new Dictionary<int, string> { [60] = "A" },
        };

        await store.SaveSettingsAsync(settings);
        var loaded = await store.LoadSettingsAsync();

        Assert.Equal("dark", loaded.Theme);
        Assert.False(loaded.AutoPlayNext);
        Assert.Equal("profile-1", loaded.SelectedGameProfileId);
        Assert.Equal(InstrumentKind.Microphone, loaded.Mapping.Instrument);
        Assert.Equal(InputMode.WindowMessage, loaded.Mapping.InputMode);
        Assert.Equal(-120, loaded.Mapping.LinkLatencyMs);
        Assert.Equal("Q", loaded.PianoKeyMap![60]);
        Assert.Equal("E", loaded.DrumKeyMap![36]);
        Assert.Equal("A", loaded.MicrophoneKeyMap![60]);
    }

    [Fact]
    public async Task GameProfiles_ParseLegacySnakeCaseAndIntegerKeyMap()
    {
        var profilesDirectory = Path.Combine(_directory, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "test.json"), """
            {
              "id": "game-piano",
              "name": "Game Piano",
              "instrument": "piano",
              "note_range_low": 48,
              "note_range_high": 83,
              "prefer_nearest_white": false,
              "max_polyphony": 24,
              "chord_mode": "off",
              "custom_key_map": { "48": ",", "49": "L" }
            }
            """);

        var profile = Assert.Single(await new GameProfileProvider(profilesDirectory).LoadAsync());

        Assert.Equal("game-piano", profile.Id);
        Assert.Equal(24, profile.Config.MaxPolyphony);
        Assert.False(profile.Config.PreferNearestWhite);
        Assert.Equal(",", profile.Config.CustomKeyMap![48]);
    }

    [Fact]
    public async Task Library_ScansSearchesPagesAndPersistsMidiFiles()
    {
        var musicDirectory = Path.Combine(_directory, "中文曲库");
        Directory.CreateDirectory(musicDirectory);
        await File.WriteAllBytesAsync(Path.Combine(musicDirectory, "测试曲.mid"), CreateMidi());
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "data"));
        var library = new MidiLibraryService(new DryWetMidiFileReader(), store);
        await library.InitializeAsync();

        var added = await library.ScanFolderAsync(musicDirectory);
        var page = library.GetPage(0, 10, query: "测试");

        Assert.Single(added);
        var entry = Assert.Single(page.Entries);
        Assert.Equal(500, entry.DurationMs);
        Assert.Equal("中文曲库", entry.Folder);

        var reloaded = new MidiLibraryService(new DryWetMidiFileReader(), store);
        await reloaded.InitializeAsync();
        Assert.Single(reloaded.GetPage(0, 10).Entries);
    }

    [Fact]
    public async Task LegacyImporter_UsesLatestLevelDbJsonAndKeepsBackup()
    {
        var legacy = Path.Combine(_directory, "legacy", "Local Storage", "leveldb");
        Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "000003.log"),
            "binary devspace-autoplayer-config {\"speed\":0.8,\"transpose\":1} " +
            "devspace-autoplayer-config {\"speed\":1.4,\"transpose\":3,\"selectedGameId\":\"identity-v-piano\"} " +
            "devspace-autoplayer-presets [{\"id\":\"p1\",\"name\":\"旧预设\",\"createdAt\":1,\"config\":{\"speed\":1.2}}] " +
            "devspace-autoplayer-playlist [{\"id\":\"q1\",\"path\":\"C:\\\\song.mid\",\"name\":\"曲目\",\"addedAt\":2}]");
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "new"));

        var result = await new LegacyDataImporter(store, Path.Combine(_directory, "legacy")).ImportIfNeededAsync();
        var settings = await store.LoadSettingsAsync();
        var presets = await store.LoadPresetsAsync();
        var playlist = await store.LoadPlaylistAsync();

        Assert.True(result.Imported);
        Assert.Equal(1.4, settings.Mapping.Speed);
        Assert.Equal(3, settings.Mapping.TransposeSemitones);
        Assert.Equal("identity-v-piano", settings.SelectedGameProfileId);
        Assert.Equal("旧预设", Assert.Single(presets).Name);
        Assert.Equal("C:\\song.mid", Assert.Single(playlist).Path);
        Assert.True(Directory.EnumerateFiles(Path.Combine(store.DataDirectory, "migration-backups"), "*.log", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Library_ImportsLegacyJsonAndBacksItUpWithoutDeletingSource()
    {
        var legacyPath = Path.Combine(_directory, "old", "midi_library.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, """
            {"entries":{"abc":{"id":"abc","path":"C:\\music\\song.mid","name":"song","folder":"old","duration_ms":1200,"notes":4,"added_at":"2024-01-01T00:00:00+00:00"}}}
            """);
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "new-library"));
        var library = new MidiLibraryService(new DryWetMidiFileReader(), store, legacyPath);

        await library.InitializeAsync();

        var entry = Assert.Single(library.GetPage(0, 10).Entries);
        Assert.Equal("C:\\music\\song.mid", entry.Path);
        Assert.True(File.Exists(legacyPath));
        Assert.True(Directory.EnumerateFiles(Path.Combine(store.DataDirectory, "migration-backups"), "midi_library.json", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Library_DeleteSource_RemovesFileAndIndexOnlyAfterSuccess()
    {
        var midiPath = Path.Combine(_directory, "delete-me.mid");
        await File.WriteAllBytesAsync(midiPath, CreateMidi());
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "delete-source-data"));
        var library = new MidiLibraryService(new DryWetMidiFileReader(), store);
        await library.InitializeAsync();
        var entry = await library.AddAsync(midiPath);

        var deleted = await library.DeleteSourceAsync(entry!.Id);

        Assert.True(deleted);
        Assert.False(File.Exists(midiPath));
        Assert.Empty(library.GetPage(0, 10).Entries);
    }

    [Fact]
    public async Task DiagnosticsZip_ContainsEnvironmentAndLogsWithoutIncludingItself()
    {
        var store = new FileSystemUserDataStore(Path.Combine(_directory, "diagnostics"));
        var logs = Path.Combine(store.DataDirectory, "logs");
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(Path.Combine(logs, "app.log"), $"test log {Environment.MachineName} {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
        await store.SaveSettingsAsync(new UserSettings { BoundProcessName = "private-process" });
        var output = Path.Combine(store.DataDirectory, "diagnostics.zip");

        await new DiagnosticService(store).ExportAsync(output);

        using var archive = ZipFile.OpenRead(output);
        Assert.NotNull(archive.GetEntry("environment.txt"));
        Assert.NotNull(archive.GetEntry("logs/app.log"));
        Assert.Null(archive.GetEntry("diagnostics.zip"));
        Assert.Null(archive.GetEntry("settings.json"));
        var environment = archive.GetEntry("environment.txt")!;
        using var reader = new StreamReader(environment.Open());
        var summary = await reader.ReadToEndAsync();
        Assert.Contains("OS:", summary);
        Assert.Contains("Framework:", summary);
        Assert.Contains("ProcessArchitecture:", summary);
        Assert.DoesNotContain(Environment.MachineName, summary, StringComparison.OrdinalIgnoreCase);
        var log = archive.GetEntry("logs/app.log")!;
        using var logReader = new StreamReader(log.Open());
        var logText = await logReader.ReadToEndAsync();
        Assert.DoesNotContain(Environment.MachineName, logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), logText, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static byte[] CreateMidi() =>
    [
        0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
        0x00, 0x00, 0x00, 0x01, 0x01, 0xE0,
        0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x0D,
        0x00, 0x90, 0x3C, 0x64,
        0x83, 0x60, 0x80, 0x3C, 0x00,
        0x00, 0xFF, 0x2F, 0x00,
    ];
}
