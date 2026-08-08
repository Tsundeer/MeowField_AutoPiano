using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.App;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IGameProfileProvider _profiles;
    private readonly IUserDataStore _store;
    private string? _pendingProfileId;

    public ProfilesViewModel(IGameProfileProvider profiles, IUserDataStore store)
    {
        _profiles = profiles;
        _store = store;
        _ = InitializeAsync();
    }

    public ObservableCollection<GameProfile> GameProfiles { get; } = [];
    public ObservableCollection<Preset> Presets { get; } = [];
    public ObservableCollection<KeyMappingRow> KeyMappings { get; } = [];

    [ObservableProperty] private GameProfile? selectedProfile;
    [ObservableProperty] private Preset? selectedPreset;
    [ObservableProperty] private string presetName = "";
    [ObservableProperty] private string statusText = "正在加载配置档案...";

    public Func<MappingConfig>? CurrentConfigProvider { get; set; }
    public event EventHandler<MappingConfig>? ConfigSelected;

    [RelayCommand]
    private void LoadCurrentKeyMap()
    {
        if (CurrentConfigProvider is null) return;
        PopulateKeyMappings(CurrentConfigProvider());
        StatusText = "已载入当前键位映射";
    }

    [RelayCommand]
    private void ApplyKeyMap()
    {
        if (CurrentConfigProvider is null) return;
        var current = CurrentConfigProvider();
        var values = KeyMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Key))
            .ToDictionary(row => row.Note, row => row.Key.Trim().ToUpperInvariant());
        var custom = values.Count == 0 ? null : values;
        ConfigSelected?.Invoke(this, current with { CustomKeyMap = custom });
        StatusText = $"已应用 {values.Count} 个自定义键位";
    }

    [RelayCommand]
    private void ResetKeyMap()
    {
        if (CurrentConfigProvider is null) return;
        var current = CurrentConfigProvider();
        ConfigSelected?.Invoke(this, current with { CustomKeyMap = null });
        PopulateKeyMappings(current with { CustomKeyMap = null });
        StatusText = current.Instrument == InstrumentKind.Drums ? "已恢复 GM 架子鼓映射" : "已恢复默认键位映射";
    }

    private async Task InitializeAsync()
    {
        try
        {
            foreach (var profile in await _profiles.LoadAsync()) GameProfiles.Add(profile);
            SelectProfileById(_pendingProfileId);
            foreach (var preset in await _store.LoadPresetsAsync()) Presets.Add(preset);
            StatusText = $"已加载 {GameProfiles.Count} 个档案，{Presets.Count} 个预设";
        }
        catch (Exception exception)
        {
            StatusText = $"配置加载失败：{exception.Message}";
        }
    }

    public void SelectProfileById(string? id)
    {
        _pendingProfileId = id;
        if (string.IsNullOrWhiteSpace(id)) return;
        SelectedProfile = GameProfiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshKeyMappings(MappingConfig config) => PopulateKeyMappings(config);

    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile is null) return;
        ConfigSelected?.Invoke(this, SelectedProfile.Config);
        StatusText = $"已应用：{SelectedProfile.Name}";
    }

    [RelayCommand]
    private void ApplyPreset()
    {
        if (SelectedPreset is null) return;
        ConfigSelected?.Invoke(this, SelectedPreset.Config);
        StatusText = $"已应用预设：{SelectedPreset.Name}";
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(PresetName) || CurrentConfigProvider is null)
        {
            StatusText = "请输入预设名称";
            return;
        }

        var preset = new Preset(Guid.NewGuid().ToString("N"), PresetName.Trim(), CurrentConfigProvider(), DateTimeOffset.Now);
        Presets.Add(preset);
        await _store.SavePresetsAsync(Presets.ToArray());
        PresetName = "";
        StatusText = $"已保存预设：{preset.Name}";
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset is null) return;
        if (MessageBox.Show($"删除预设“{SelectedPreset.Name}”？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        await _store.SavePresetsAsync(Presets.ToArray());
        StatusText = "预设已删除";
    }

    [RelayCommand]
    private async Task OverwritePresetAsync()
    {
        if (SelectedPreset is null || CurrentConfigProvider is null) return;
        var index = Presets.IndexOf(SelectedPreset);
        var updated = SelectedPreset with { Config = CurrentConfigProvider() };
        Presets[index] = updated;
        SelectedPreset = updated;
        await _store.SavePresetsAsync(Presets.ToArray());
        StatusText = $"已覆盖预设：{updated.Name}";
    }

    private void PopulateKeyMappings(MappingConfig config)
    {
        KeyMappings.Clear();
        IEnumerable<int> notes = config.Instrument switch
        {
            InstrumentKind.Drums => (config.CustomKeyMap?.Keys ?? NoteMapping.DrumKeys.Keys).Order(),
            _ => Enumerable.Range(config.NoteRangeLow, config.NoteRangeHigh - config.NoteRangeLow + 1),
        };
        var defaults = config.Instrument switch
        {
            InstrumentKind.Drums => NoteMapping.DrumKeys,
            InstrumentKind.Piano => NoteMapping.PianoKeys,
            _ => Enumerable.Range(NoteMapping.MicrophoneMinMidi, NoteMapping.MicrophoneKeys.Count)
                .ToDictionary(note => note, note => NoteMapping.MicrophoneKeys[note - NoteMapping.MicrophoneMinMidi]),
        };
        foreach (var note in notes)
        {
            var key = config.CustomKeyMap?.GetValueOrDefault(note) ?? defaults.GetValueOrDefault(note) ?? "";
            KeyMappings.Add(new KeyMappingRow(note, MidiNoteName(note), key));
        }
    }

    private static string MidiNoteName(int note)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return $"{names[note % 12]}{note / 12 - 1}";
    }
}

public partial class KeyMappingRow(int note, string noteName, string key) : ObservableObject
{
    public int Note { get; } = note;
    public string NoteName { get; } = noteName;
    [ObservableProperty] private string key = key;
}
