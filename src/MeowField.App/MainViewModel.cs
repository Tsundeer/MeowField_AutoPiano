using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using MeowField.Domain;
using Microsoft.Win32;

namespace MeowField.App;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IMidiFileReader _midiReader;
    private readonly IWindowCatalog _windowCatalog;
    private readonly IPlaybackEngine _playback;
    private readonly IUserDataStore _store;
    private readonly ILegacyDataImporter _legacyImporter;
    private readonly ScheduleViewModel _schedule;
    private ParsedMidi? _midi;
    private bool _completionHandled;
    private bool _restoring;
    private CancellationTokenSource? _saveCancellation;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _configRefreshCancellation;
    private IReadOnlyDictionary<int, string>? _pianoKeyMap;
    private IReadOnlyDictionary<int, string>? _drumKeyMap;
    private IReadOnlyDictionary<int, string>? _microphoneKeyMap;
    private (int Low, int High) _pianoRange = (48, 83);
    private (int Low, int High) _drumRange = (0, 127);
    private (int Low, int High) _microphoneRange = (NoteMapping.MicrophoneMinMidi, NoteMapping.MicrophoneMaxMidi);
    private bool _applyingConfig;

    public MainViewModel(
        IMidiFileReader midiReader,
        IWindowCatalog windowCatalog,
        IPlaybackEngine playback,
        IUserDataStore store,
        ILegacyDataImporter legacyImporter,
        LibraryViewModel library,
        OnlineLibraryViewModel onlineLibrary,
        ProfilesViewModel profiles,
        ConverterViewModel converter,
        ScheduleViewModel schedule,
        DiagnosticsViewModel diagnostics)
    {
        _midiReader = midiReader;
        _windowCatalog = windowCatalog;
        _playback = playback;
        _store = store;
        _legacyImporter = legacyImporter;
        _schedule = schedule;
        Library = library;
        OnlineLibrary = onlineLibrary;
        Profiles = profiles;
        Converter = converter;
        Schedule = schedule;
        Diagnostics = diagnostics;
        Library.LoadRequested += (_, path) => _ = LoadMidiAsync(path);
        Library.QueueRequested += (_, entry) => AddToQueue(entry);
        OnlineLibrary.MidiLoaded += (_, payload) => _ = LoadOnlineMidiAsync(payload);
        Converter.MidiReady += (_, path) => _ = LoadMidiAsync(path);
        Profiles.ConfigSelected += (_, config) => { ApplyConfig(config); Profiles.RefreshKeyMappings(config); };
        Schedule.Due += (_, scheduleToPlay) => _ = OnScheduleDueAsync(scheduleToPlay);
        Profiles.CurrentConfigProvider = CreateConfig;
        Profiles.RefreshKeyMappings(CreateConfig());
        Schedule.MidiPathProvider = () => _midi?.SourcePath;
        _playback.SnapshotChanged += OnSnapshotChanged;
        IsAdministrator = windowCatalog.IsAdministrator;
        RefreshWindows();
        _ = RestoreAsync();
    }

    public LibraryViewModel Library { get; }
    public OnlineLibraryViewModel OnlineLibrary { get; }
    public ProfilesViewModel Profiles { get; }
    public ConverterViewModel Converter { get; }
    public ScheduleViewModel Schedule { get; }
    public DiagnosticsViewModel Diagnostics { get; }

    public ObservableCollection<WindowTarget> Windows { get; } = [];
    public ObservableCollection<PlaylistItem> Queue { get; } = [];
    public IReadOnlyList<InputMode> InputModes { get; } = Enum.GetValues<InputMode>();
    public IReadOnlyList<InstrumentKind> Instruments { get; } = Enum.GetValues<InstrumentKind>();
    public IReadOnlyList<ChordMode> ChordModes { get; } = Enum.GetValues<ChordMode>();
    public ObservableCollection<KeyboardKeyState> KeyboardKeys { get; } = new(
        KeyboardLayout.PianoPreviewKeys
            .Select(key => new KeyboardKeyState(key)));

    [ObservableProperty] private WindowTarget? selectedWindow;
    [ObservableProperty] private string? targetProcessName;
    [ObservableProperty] private string fileName = "尚未载入 MIDI";
    [ObservableProperty] private string filePath = "请选择文件，或将 MIDI 拖入窗口";
    [ObservableProperty] private string statusText = "准备就绪";
    [ObservableProperty] private string elapsedText = "00:00.000";
    [ObservableProperty] private string durationText = "00:00.000";
    [ObservableProperty] private int durationMilliseconds;
    [ObservableProperty] private int elapsedMilliseconds;
    [ObservableProperty] private string activeKeysText = "-";
    [ObservableProperty] private double progress;
    [ObservableProperty] private double speed = 1;
    [ObservableProperty] private int transpose;
    [ObservableProperty] private int noteRangeLow = 48;
    [ObservableProperty] private int noteRangeHigh = 83;
    [ObservableProperty] private int maxPolyphony = 10;
    [ObservableProperty] private int linkLatencyMs;
    [ObservableProperty] private bool preferNearestWhite = true;
    [ObservableProperty] private bool autoTranspose;
    [ObservableProperty] private InputMode inputMode = InputMode.SendInput;
    [ObservableProperty] private InstrumentKind instrument = InstrumentKind.Piano;
    [ObservableProperty] private ChordMode chordMode = ChordMode.Prefer;
    [ObservableProperty] private PlaybackState playbackState = PlaybackState.Idle;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int noteCount;
    [ObservableProperty] private int eventCount;
    [ObservableProperty] private double? whiteKeyRatio;
    [ObservableProperty] private double? originalWhiteKeyRatio;
    [ObservableProperty] private bool isAdministrator;
    [ObservableProperty] private IReadOnlyDictionary<int, string>? customKeyMap;
    [ObservableProperty] private IReadOnlyList<MidiNote> timelineNotes = [];
    [ObservableProperty] private string activePage = "playback";
    [ObservableProperty] private bool autoPlayNext = true;
    [ObservableProperty] private bool isEnglish;
    [ObservableProperty] private bool isDarkTheme;
    [ObservableProperty] private PlaylistItem? selectedQueueItem;
    [ObservableProperty] private string updateStatus = "尚未检查更新";
    [ObservableProperty] private string latestVersion = "";
    [ObservableProperty] private string releaseUrl = "https://github.com/Tsundeer/MeowField_AutoPiano/releases";

    public string PlayPauseLabel => IsEnglish ? (PlaybackState == PlaybackState.Playing ? "Pause" : "Play") : (PlaybackState == PlaybackState.Playing ? "暂停" : "播放");
    public string LanguageLabel => IsEnglish ? "中" : "EN";
    public bool HasMidi => _midi is not null;
    public string PermissionText => IsEnglish ? (IsAdministrator ? "Administrator" : "Standard user") : (IsAdministrator ? "管理员模式" : "普通权限");
    public string NoteCountLabel => IsEnglish ? $"{NoteCount:N0} notes" : $"{NoteCount:N0} 音符";
    public string EventCountLabel => IsEnglish ? $"{EventCount:N0} events" : $"{EventCount:N0} 事件";
    public string TransposeLabel => IsEnglish ? $"{Transpose:+0;-0;0} semitones" : $"{Transpose:+0;-0;0} 半音";
    public string LinkLatencyLabel => $"{LinkLatencyMs} ms";
    public string FitRatioLabel => WhiteKeyRatio is null ? "-" : $"{WhiteKeyRatio:0.0}%";
    public string OriginalFitRatioLabel => OriginalWhiteKeyRatio is null ? "-" : $"{OriginalWhiteKeyRatio:0.0}%";
    public string OriginalFitRatioSummary => IsEnglish ? $"Original {OriginalFitRatioLabel}" : $"原始 {OriginalFitRatioLabel}";
    public bool IsPlaybackVisible => ActivePage == "playback";
    public bool IsLibraryVisible => ActivePage == "library";
    public bool IsOnlineLibraryVisible => ActivePage == "online-library";
    public bool IsScheduleVisible => ActivePage == "schedule";
    public bool IsConverterVisible => ActivePage == "converter";
    public bool IsProfilesVisible => ActivePage == "profiles";
    public bool IsDiagnosticsVisible => ActivePage == "diagnostics";
    public bool IsSettingsVisible => ActivePage == "settings";
    public string SoftwareName => "MeowField_AutoPiano";
    public string CurrentVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
    public string VersionText => $"版本 {CurrentVersion}";
    public string VersionBadge => $"v{CurrentVersion}";
    public bool CanPlayPrevious => SelectedQueueItem is not null && Queue.IndexOf(SelectedQueueItem) > 0;
    public bool CanPlayNext => SelectedQueueItem is null ? Queue.Count > 0 : Queue.IndexOf(SelectedQueueItem) < Queue.Count - 1;
    public bool CanMoveQueueUp => SelectedQueueItem is not null && Queue.IndexOf(SelectedQueueItem) > 0;
    public bool CanMoveQueueDown => SelectedQueueItem is not null && Queue.IndexOf(SelectedQueueItem) >= 0 && Queue.IndexOf(SelectedQueueItem) < Queue.Count - 1;
    public bool CanEditPlaybackSettings => PlaybackState is not (PlaybackState.Playing or PlaybackState.Paused);

    [RelayCommand]
    private async Task OpenMidiAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            await LoadMidiAsync(dialog.FileName);
        }
    }

    public async Task LoadMidiAsync(string path, bool addToQueue = true, string? displayName = null)
    {
        try
        {
            IsBusy = true;
            StatusText = "正在解析 MIDI...";
            var midi = await _midiReader.ReadAsync(path);
            var config = CreateConfig();
            if (AutoTranspose && midi.Notes.Count > 0)
            {
                Transpose = NoteMapping.FindOptimalTranspose(midi.Notes.Select(note => note.Note), config);
                config = CreateConfig();
            }

            _midi = midi;
            TimelineNotes = midi.Notes;
            _completionHandled = false;
            if (addToQueue) EnsureQueueItem(path, Path.GetFileNameWithoutExtension(path));
            RaiseQueueState();
            _playback.Load(midi, config);
            FileName = displayName ?? Path.GetFileNameWithoutExtension(path);
            FilePath = displayName is null ? Path.GetFullPath(path) : "在线曲库";
            NoteCount = midi.Notes.Count;
            EventCount = PlaybackEventBuilder.Build(midi.Notes, config).Count;
            UpdateFitRatios();
            OnPropertyChanged(nameof(NoteCountLabel));
            OnPropertyChanged(nameof(EventCountLabel));
            DurationText = FormatTime(_playback.Snapshot.DurationMs);
            DurationMilliseconds = _playback.Snapshot.DurationMs;
            StatusText = $"已载入 {NoteCount:N0} 个音符";
            OnPropertyChanged(nameof(HasMidi));
        }
        catch (Exception exception)
        {
            StatusText = $"载入失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadOnlineMidiAsync(OnlineMidiPayload payload)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"MeowField-{Guid.NewGuid():N}.mid");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, payload.Content);
            await LoadMidiAsync(temporaryPath, addToQueue: false, displayName: payload.Name);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { /* The OS will clean a locked temporary file. */ }
        }
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        var selectedHandle = SelectedWindow?.Handle;
        Windows.Clear();
        foreach (var target in _windowCatalog.ListVisibleWindows())
        {
            Windows.Add(target);
        }

        SelectedWindow = selectedHandle is not null
            ? Windows.FirstOrDefault(item => item.Handle == selectedHandle)
            : Windows.FirstOrDefault(item => !string.IsNullOrWhiteSpace(TargetProcessName) && string.Equals(item.ProcessName, TargetProcessName, StringComparison.OrdinalIgnoreCase));
        StatusText = Windows.Count == 0 ? "未找到可绑定窗口" : $"已发现 {Windows.Count} 个窗口";
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        if (_midi is null)
        {
            StatusText = "请先载入 MIDI";
            return;
        }

        if (PlaybackState == PlaybackState.Playing)
        {
            _playback.Pause();
            return;
        }

        if (InputMode == InputMode.WindowMessage && SelectedWindow is null)
        {
            StatusText = "窗口消息模式需要先绑定目标窗口";
            return;
        }

        if (InputMode == InputMode.SendInput && SelectedWindow is not null)
        {
            if (!_windowCatalog.TryActivate(SelectedWindow.Handle))
            {
                StatusText = "无法激活目标窗口。请关闭遮挡窗口，并确认程序权限一致。";
                return;
            }

            await Task.Delay(120);
            if (!_windowCatalog.IsForeground(SelectedWindow.Handle))
            {
                StatusText = "目标窗口未进入前台。请点击目标游戏窗口后，使用全局播放快捷键。";
                return;
            }
        }

        if (PlaybackState is PlaybackState.Loaded or PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _playback.Load(_midi, CreateConfig());
        }

        _playback.Play(SelectedWindow?.Handle ?? 0);
    }

    [RelayCommand]
    private void Stop() => _playback.Stop();

    [RelayCommand]
    private void Navigate(string page)
    {
        ActivePage = string.IsNullOrWhiteSpace(page) ? "playback" : page;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = IsEnglish ? "Checking for updates..." : "正在检查更新...";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeowField-AutoPiano", VersionText));
            using var response = await client.GetAsync("https://api.github.com/repos/Tsundeer/MeowField_AutoPiano/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                UpdateStatus = IsEnglish ? $"Update check failed ({(int)response.StatusCode})" : $"检查更新失败（{(int)response.StatusCode}）";
                return;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var url = document.RootElement.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(url)) ReleaseUrl = url;
            LatestVersion = tag ?? string.Empty;
            var comparison = CompareVersions(tag, CurrentVersion);
            UpdateStatus = comparison > 0
                ? (IsEnglish ? $"New version available: {tag}" : $"发现新版本：{tag}")
                : (IsEnglish ? "You are up to date" : "当前已是最新版本");
        }
        catch (Exception exception)
        {
            UpdateStatus = IsEnglish ? $"Update check failed: {exception.Message}" : $"检查更新失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        Process.Start(new ProcessStartInfo { FileName = ReleaseUrl, UseShellExecute = true });
    }

    private static string NormalizeVersion(string? value) => (value ?? string.Empty).Trim().TrimStart('v', 'V');

    private static int CompareVersions(string? releaseVersion, string currentVersion)
    {
        return Version.TryParse(NormalizeVersion(releaseVersion), out var latest)
               && Version.TryParse(NormalizeVersion(currentVersion), out var current)
            ? latest.CompareTo(current)
            : 0;
    }

    [RelayCommand]
    private void ToggleLanguage() => IsEnglish = !IsEnglish;

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    public void TogglePlayPause() => _ = PlayPause();
    public void StopPlayback() => Stop();
    public void OpenSettings() => Navigate("settings");

    public MappingConfig CreateConfig() => new()
    {
        Instrument = Instrument,
        InputMode = InputMode,
        MidiChannelFilter = Instrument == InstrumentKind.Drums ? 9 : null,
        NoteRangeLow = NoteRangeLow,
        NoteRangeHigh = NoteRangeHigh,
        PreferNearestWhite = PreferNearestWhite,
        TransposeSemitones = Transpose,
        Speed = Speed,
        MaxPolyphony = MaxPolyphony,
        ChordMode = ChordMode,
        AutoTranspose = AutoTranspose,
        LinkLatencyMs = LinkLatencyMs,
        CustomKeyMap = CustomKeyMap,
    };

    public void Dispose()
    {
        _playback.SnapshotChanged -= OnSnapshotChanged;
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _configRefreshCancellation?.Cancel();
        _configRefreshCancellation?.Dispose();
        _saveGate.Dispose();
    }

    private void ApplyConfig(MappingConfig config)
    {
        _applyingConfig = true;
        try
        {
            Instrument = config.Instrument;
            InputMode = config.InputMode;
            Transpose = config.TransposeSemitones;
            NoteRangeLow = config.NoteRangeLow;
            NoteRangeHigh = config.NoteRangeHigh;
            Speed = config.Speed;
            MaxPolyphony = config.MaxPolyphony;
            ChordMode = config.ChordMode;
            PreferNearestWhite = config.PreferNearestWhite;
            AutoTranspose = config.AutoTranspose;
            LinkLatencyMs = config.LinkLatencyMs;
            CustomKeyMap = config.CustomKeyMap;
            SaveInstrumentRange(config.Instrument, config.NoteRangeLow, config.NoteRangeHigh);
        }
        finally
        {
            _applyingConfig = false;
        }
        switch (Instrument)
        {
            case InstrumentKind.Piano: _pianoKeyMap = CustomKeyMap; break;
            case InstrumentKind.Drums: _drumKeyMap = CustomKeyMap; break;
            case InstrumentKind.Microphone: _microphoneKeyMap = CustomKeyMap; break;
        }
        if (_midi is not null)
        {
            _playback.Load(_midi, CreateConfig());
            EventCount = PlaybackEventBuilder.Build(_midi.Notes, CreateConfig()).Count;
            UpdateFitRatios();
        }
    }

    private async Task OnScheduleDueAsync(ScheduledPlayback schedule)
    {
        var operation = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await LoadMidiAsync(schedule.MidiPath);
            await PlayPause();
        });
        await operation.Task.Unwrap();
    }

    partial void OnActivePageChanged(string value)
    {
        OnPropertyChanged(nameof(IsPlaybackVisible));
        OnPropertyChanged(nameof(IsLibraryVisible));
        OnPropertyChanged(nameof(IsOnlineLibraryVisible));
        OnPropertyChanged(nameof(IsScheduleVisible));
        OnPropertyChanged(nameof(IsConverterVisible));
        OnPropertyChanged(nameof(IsProfilesVisible));
        OnPropertyChanged(nameof(IsDiagnosticsVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        if (System.Windows.Application.Current?.MainWindow is { } window)
        {
            window.Dispatcher.BeginInvoke(() => LocalizationService.Apply(window, IsEnglish));
        }
    }

    partial void OnIsEnglishChanged(bool value)
    {
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(PlayPauseLabel));
        OnPropertyChanged(nameof(PermissionText));
        OnPropertyChanged(nameof(NoteCountLabel));
        OnPropertyChanged(nameof(EventCountLabel));
        OnPropertyChanged(nameof(TransposeLabel));
        OnPropertyChanged(nameof(OriginalFitRatioSummary));
        if (_midi is null)
        {
            FileName = value ? "No MIDI loaded" : "尚未载入 MIDI";
            FilePath = value ? "Choose a file or drop a MIDI into the window" : "请选择文件，或将 MIDI 拖入窗口";
            StatusText = value ? "Ready" : "准备就绪";
        }
        if (System.Windows.Application.Current?.MainWindow is { } window)
        {
            LocalizationService.Apply(window, value);
        }
        ScheduleSettingsSave();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.Apply(value);
        ScheduleSettingsSave();
    }

    partial void OnSelectedQueueItemChanged(PlaylistItem? value) => RaiseQueueState();

    partial void OnSelectedWindowChanged(WindowTarget? value)
    {
        if (value is not null) TargetProcessName = value.ProcessName;
        ScheduleSettingsSave();
    }

    partial void OnSpeedChanged(double value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnTransposeChanged(int value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); OnPropertyChanged(nameof(TransposeLabel)); }
    partial void OnMaxPolyphonyChanged(int value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnLinkLatencyMsChanged(int value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnPreferNearestWhiteChanged(bool value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnAutoTransposeChanged(bool value) => ScheduleSettingsSave();
    partial void OnInputModeChanged(InputMode value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnChordModeChanged(ChordMode value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnAutoPlayNextChanged(bool value) => ScheduleSettingsSave();
    partial void OnCustomKeyMapChanged(IReadOnlyDictionary<int, string>? value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnTargetProcessNameChanged(string? value) => ScheduleSettingsSave();
    partial void OnNoteRangeLowChanged(int value) { if (value > NoteRangeHigh) NoteRangeHigh = value; ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnNoteRangeHighChanged(int value) { if (value < NoteRangeLow) NoteRangeLow = value; ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnWhiteKeyRatioChanged(double? value) => OnPropertyChanged(nameof(FitRatioLabel));
    partial void OnOriginalWhiteKeyRatioChanged(double? value) => OnPropertyChanged(nameof(OriginalFitRatioLabel));

    partial void OnInstrumentChanging(InstrumentKind oldValue, InstrumentKind newValue)
    {
        switch (oldValue)
        {
            case InstrumentKind.Piano: _pianoKeyMap = CustomKeyMap; break;
            case InstrumentKind.Drums: _drumKeyMap = CustomKeyMap; break;
            case InstrumentKind.Microphone: _microphoneKeyMap = CustomKeyMap; break;
        }
        if (!_applyingConfig) SaveInstrumentRange(oldValue, NoteRangeLow, NoteRangeHigh);
    }

    partial void OnInstrumentChanged(InstrumentKind value)
    {
        CustomKeyMap = value switch
        {
            InstrumentKind.Piano => _pianoKeyMap,
            InstrumentKind.Drums => _drumKeyMap,
            InstrumentKind.Microphone => _microphoneKeyMap,
            _ => null,
        };
        if (!_applyingConfig)
        {
            var range = value switch
            {
                InstrumentKind.Piano => _pianoRange,
                InstrumentKind.Drums => _drumRange,
                InstrumentKind.Microphone => _microphoneRange,
                _ => _pianoRange,
            };
            NoteRangeLow = range.Low;
            NoteRangeHigh = range.High;
            if (_midi is not null)
            {
                _playback.Load(_midi, CreateConfig());
                EventCount = PlaybackEventBuilder.Build(_midi.Notes, CreateConfig()).Count;
                OnPropertyChanged(nameof(EventCountLabel));
                UpdateFitRatios();
            }
        }
        ScheduleSettingsSave();
    }

    private void SaveInstrumentRange(InstrumentKind instrumentKind, int low, int high)
    {
        switch (instrumentKind)
        {
            case InstrumentKind.Piano: _pianoRange = (low, high); break;
            case InstrumentKind.Drums: _drumRange = (low, high); break;
            case InstrumentKind.Microphone: _microphoneRange = (low, high); break;
        }
    }

    private void SchedulePlaybackConfigRefresh()
    {
        if (_restoring || _applyingConfig || _midi is null || !CanEditPlaybackSettings) return;
        _configRefreshCancellation?.Cancel();
        _configRefreshCancellation?.Dispose();
        _configRefreshCancellation = new CancellationTokenSource();
        _ = RefreshPlaybackConfigAsync(_configRefreshCancellation.Token);
    }

    private async Task RefreshPlaybackConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_midi is null || !CanEditPlaybackSettings) return;
                var config = CreateConfig();
                _playback.Load(_midi, config);
                EventCount = PlaybackEventBuilder.Build(_midi.Notes, config).Count;
                OnPropertyChanged(nameof(EventCountLabel));
                UpdateFitRatios();
            });
        }
        catch (OperationCanceledException)
        {
            // A newer parameter value superseded this preview refresh.
        }
    }

    private void UpdateFitRatios()
    {
        if (_midi is null || _midi.Notes.Count == 0)
        {
            WhiteKeyRatio = null;
            OriginalWhiteKeyRatio = null;
        }
        else if (PreferNearestWhite)
        {
            WhiteKeyRatio = NoteMapping.CalculateWhiteKeyRatio(_midi.Notes.Select(note => note.Note), Transpose) * 100;
            OriginalWhiteKeyRatio = NoteMapping.CalculateWhiteKeyRatio(_midi.Notes.Select(note => note.Note)) * 100;
        }
        else
        {
            WhiteKeyRatio = NoteMapping.CalculateRangeFitRatio(_midi.Notes.Select(note => note.Note), NoteRangeLow, NoteRangeHigh, Transpose) * 100;
            OriginalWhiteKeyRatio = NoteMapping.CalculateRangeFitRatio(_midi.Notes.Select(note => note.Note), NoteRangeLow, NoteRangeHigh) * 100;
        }
        OnPropertyChanged(nameof(FitRatioLabel));
        OnPropertyChanged(nameof(OriginalFitRatioLabel));
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            PlaybackState = snapshot.State;
            OnPropertyChanged(nameof(CanEditPlaybackSettings));
            Progress = snapshot.Progress;
            ElapsedText = FormatTime(snapshot.CursorMs);
            ElapsedMilliseconds = snapshot.CursorMs;
            DurationText = FormatTime(snapshot.DurationMs);
            DurationMilliseconds = snapshot.DurationMs;
            ActiveKeysText = snapshot.ActiveKeys.Count == 0 ? "-" : string.Join("  ", snapshot.ActiveKeys.Order(StringComparer.Ordinal));
            foreach (var key in KeyboardKeys) key.IsActive = snapshot.ActiveKeys.Contains(key.Label);
            StatusText = snapshot.State switch
            {
                PlaybackState.Playing => "正在播放",
                PlaybackState.Paused => "已暂停",
                PlaybackState.Stopped when snapshot.CursorMs == snapshot.DurationMs => "播放完成",
                PlaybackState.Stopped => "已停止",
                PlaybackState.Faulted => $"播放失败：{DescribePlaybackError(snapshot.Error)}",
                _ => StatusText,
            };
            if (snapshot.State == PlaybackState.Stopped && snapshot.DurationMs > 0 && snapshot.CursorMs == snapshot.DurationMs && AutoPlayNext && !_completionHandled)
            {
                _completionHandled = true;
                _ = PlayQueueOffsetAsync(1);
            }
            OnPropertyChanged(nameof(PlayPauseLabel));
        });
    }

    private static string FormatTime(int milliseconds) => TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"mm\:ss\.fff");

    private static string DescribePlaybackError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "未知错误";
        if (error.Contains("bound target is not the foreground", StringComparison.OrdinalIgnoreCase))
            return "目标窗口不在前台。请切换回游戏后重试。";
        if (error.Contains("target window is no longer available", StringComparison.OrdinalIgnoreCase))
            return "目标窗口已关闭，请刷新并重新绑定。";
        if (error.Contains("No foreground window", StringComparison.OrdinalIgnoreCase))
            return "未找到前台窗口，请切换到目标游戏后重试。";
        return error;
    }
}

public partial class KeyboardKeyState(string label) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] private bool isActive;
}
