using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    private bool _profileApplied;
    private bool _syncingInstrumentChoice;
    private bool _timelineDragging;

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
        Profiles.ConfigSelected += (_, config) =>
        {
            var profile = Profiles.SelectedProfile;
            if (profile is not null && ReferenceEquals(profile.Config, config))
            {
                ApplyGameProfile(profile);
            }
            else
            {
                _profileApplied = false;
                Profiles.ClearSelectedProfile();
                ApplyConfig(config);
                Profiles.RefreshKeyMappings(config);
                SyncSelectedInstrumentChoice();
            }
        };
        Schedule.Due += (_, scheduleToPlay) => _ = OnScheduleDueAsync(scheduleToPlay);
        Profiles.CurrentConfigProvider = CreateConfig;
        Profiles.RefreshKeyMappings(CreateConfig());
        foreach (var kind in Enum.GetValues<InstrumentKind>())
        {
            InstrumentChoices.Add(new InstrumentChoice(kind, null));
        }
        selectedInstrumentChoice = InstrumentChoices.FirstOrDefault(choice => choice.IsGeneric && choice.Kind == InstrumentKind.Piano);
        Profiles.GameProfiles.CollectionChanged += OnGameProfilesCollectionChanged;
        Profiles.PropertyChanged += OnProfilesPropertyChanged;
        RebuildKeyboardPreview(CreateConfig());
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
    public ObservableCollection<InstrumentChoice> InstrumentChoices { get; } = [];
    [ObservableProperty] private InstrumentChoice? selectedInstrumentChoice;
    public IReadOnlyList<ChordMode> ChordModes { get; } = Enum.GetValues<ChordMode>();
    public IReadOnlyList<CollisionStrategy> CollisionStrategies { get; } = Enum.GetValues<CollisionStrategy>();
    public ObservableCollection<KeyboardKeyState> KeyboardKeys { get; } = [];
    [ObservableProperty] private string keyboardCountText = "21 键";
    [ObservableProperty] private int keyboardColumns = 7;
    [ObservableProperty] private int keyboardRows = 3;

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
    [ObservableProperty] private double seekPosition;
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
    [ObservableProperty] private CollisionStrategy collisionStrategy = CollisionStrategy.PerNoteMinimal;
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
    [ObservableProperty] private double updateProgress;
    [ObservableProperty] private bool isUpdateDownloading;
    [ObservableProperty] private bool isUpdateProgressIndeterminate;
    [ObservableProperty] private string updateProgressText = "";

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
                Transpose = NoteMapping.FindOptimalTranspose(midi.Notes, config);
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

    public void SeekFromTimeline(double value) => _playback.Seek((int)Math.Round(value));
    public void BeginTimelineSeek() => _timelineDragging = true;
    public void EndTimelineSeek() => _timelineDragging = false;

    [RelayCommand]
    private void Navigate(string page)
    {
        ActivePage = string.IsNullOrWhiteSpace(page) ? "playback" : page;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = IsEnglish ? "Checking for updates..." : "正在检查更新...";
        string? installerPath = null;
        try
        {
            string? tag = null;
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"MeowField-AutoPiano/{CurrentVersion}");
            // 不走 GitHub API（未认证会被 403 限流），改用 releases/latest 的 302 重定向获取最新版本。
            using (var response = await client.GetAsync(
                       "https://github.com/Tsundeer/MeowField_AutoPiano/releases/latest",
                       HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var latestUri = response.RequestMessage?.RequestUri;
                if (latestUri is null ||
                    !latestUri.AbsolutePath.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus = IsEnglish ? "Unable to read the latest release." : "无法获取最新版本信息。";
                    return;
                }

                tag = Uri.UnescapeDataString(Path.GetFileName(latestUri.AbsolutePath.TrimEnd('/')));
            }

            var comparison = CompareVersions(tag, CurrentVersion);
            if (comparison <= 0)
            {
                LatestVersion = CurrentVersion;
                UpdateStatus = IsEnglish ? "You are up to date" : "当前已经是最新版本";
                return;
            }

            var assetName = await FindSetupAssetAsync(client, tag);
            if (string.IsNullOrWhiteSpace(assetName))
            {
                UpdateStatus = IsEnglish ? "The latest setup installer was not found." : "最新版本没有找到安装包。";
                return;
            }

            var assetUrl = $"https://github.com/Tsundeer/MeowField_AutoPiano/releases/download/" +
                           $"{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}";
            LatestVersion = tag ?? string.Empty;
            UpdateStatus = IsEnglish ? $"Downloading {tag}..." : $"正在下载 {tag} 安装包...";
            var updateDirectory = Path.Combine(Path.GetTempPath(), "MeowField_AutoPiano", "updates");
            Directory.CreateDirectory(updateDirectory);
            // Use a unique path so a failed/previous updater process can never lock
            // the destination of a new download.
            installerPath = Path.Combine(
                updateDirectory,
                $"{Path.GetFileNameWithoutExtension(assetName)}-{Guid.NewGuid():N}.exe");
            using (var download = await client.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                download.EnsureSuccessStatusCode();
                var totalBytes = download.Content.Headers.ContentLength ?? 0;
                IsUpdateDownloading = true;
                IsUpdateProgressIndeterminate = totalBytes <= 0;
                UpdateProgress = 0;
                UpdateProgressText = FormatUpdateProgress(0, totalBytes);
                await using var source = await download.Content.ReadAsStreamAsync();
                await using var destination = File.Create(installerPath);
                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await source.ReadAsync(buffer)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, count));
                    read += count;
                    if (totalBytes > 0)
                    {
                        UpdateProgress = Math.Min(100, read * 100d / totalBytes);
                        UpdateProgressText = FormatUpdateProgress(read, totalBytes);
                    }
                }
                await destination.FlushAsync();
            }

            var launcherPath = Path.Combine(updateDirectory, $"apply-update-{Guid.NewGuid():N}.cmd");
            var quotedInstaller = installerPath.Replace("%", "%%");
            var quotedLauncher = launcherPath.Replace("%", "%%");
            var currentProcessId = Environment.ProcessId;
            var launcher = $"@echo off\r\n" +
                "set /a WAIT_COUNT=0\r\n" +
                ":wait_for_app\r\n" +
                $"tasklist /fi \"PID eq {currentProcessId}\" /nh | findstr /c:\"{currentProcessId}\" >nul\r\n" +
                "if errorlevel 1 goto launch_installer\r\n" +
                "set /a WAIT_COUNT+=1\r\n" +
                "if %WAIT_COUNT% GEQ 30 goto launch_installer\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "goto wait_for_app\r\n" +
                ":launch_installer\r\n" +
                $"start \"\" /wait \"{quotedInstaller}\" /NORESTART\r\n" +
                "set EXITCODE=%ERRORLEVEL%\r\n" +
                $"del /f /q \"{quotedInstaller}\" >nul 2>&1\r\n" +
                $"del /f /q \"{quotedLauncher}\" >nul 2>&1\r\n" +
                "exit /b %EXITCODE%\r\n";
            await File.WriteAllTextAsync(launcherPath, launcher, System.Text.Encoding.ASCII);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{launcherPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            UpdateStatus = IsEnglish ? "The installer is starting..." : "安装程序正在启动...";
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            if (installerPath is not null)
            {
                try { File.Delete(installerPath); } catch { }
            }
            UpdateStatus = IsEnglish ? $"Update failed: {exception.Message}" : $"更新失败：{exception.Message}";
        }
        finally
        {
            IsUpdateDownloading = false;
        }
    }

    private static async Task<string?> FindSetupAssetAsync(HttpClient client, string tag)
    {
        const string repository = "Tsundeer/MeowField_AutoPiano";
        var fallback = $"MeowField_AutoPiano-{NormalizeVersion(tag)}-win-x64-Setup.exe";
        var assetPage = $"https://github.com/{repository}/releases/expanded_assets/{Uri.EscapeDataString(tag)}";
        using var response = await client.GetAsync(assetPage);
        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        var html = await response.Content.ReadAsStringAsync();
        var pattern = $"href=\"/{repository}/releases/download/{Regex.Escape(tag)}/(?<asset>[^\"/?#]+)\"";
        for (var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
             match.Success;
             match = match.NextMatch())
        {
            var name = Uri.UnescapeDataString(match.Groups["asset"].Value);
            if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return fallback;
    }

    private static string FormatUpdateProgress(long downloaded, long total)
    {
        var downloadedMb = downloaded / 1024d / 1024d;
        return total <= 0 ? $"{downloadedMb:0.0} MB" : $"{downloadedMb:0.0} MB / {total / 1024d / 1024d:0.0} MB";
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
        CollisionStrategy = CollisionStrategy,
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
            CollisionStrategy = config.CollisionStrategy;
            PreferNearestWhite = config.PreferNearestWhite;
            AutoTranspose = config.AutoTranspose;
            LinkLatencyMs = config.LinkLatencyMs;
            CustomKeyMap = config.CustomKeyMap;
            if (!_profileApplied)
            {
                SaveInstrumentRange(config.Instrument, config.NoteRangeLow, config.NoteRangeHigh);
            }
        }
        finally
        {
            _applyingConfig = false;
        }
        if (!_profileApplied)
        {
            switch (Instrument)
            {
                case InstrumentKind.Piano: _pianoKeyMap = CustomKeyMap; break;
                case InstrumentKind.Drums: _drumKeyMap = CustomKeyMap; break;
                case InstrumentKind.Microphone: _microphoneKeyMap = CustomKeyMap; break;
            }
        }
        if (_midi is not null)
        {
            _playback.Load(_midi, CreateConfig());
            EventCount = PlaybackEventBuilder.Build(_midi.Notes, CreateConfig()).Count;
            UpdateFitRatios();
        }
        RebuildKeyboardPreview(CreateConfig());
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
        if (value == "online-library") _ = OnlineLibrary.EnsureLoadedAsync();
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

    partial void OnSpeedChanged(double value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnTransposeChanged(int value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); OnPropertyChanged(nameof(TransposeLabel)); }
    partial void OnMaxPolyphonyChanged(int value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnLinkLatencyMsChanged(int value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnPreferNearestWhiteChanged(bool value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnAutoTransposeChanged(bool value) { InvalidateProfileSelection(); ScheduleSettingsSave(); }
    partial void OnInputModeChanged(InputMode value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnChordModeChanged(ChordMode value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnCollisionStrategyChanged(CollisionStrategy value) { InvalidateProfileSelection(); ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnAutoPlayNextChanged(bool value) => ScheduleSettingsSave();
    partial void OnCustomKeyMapChanged(IReadOnlyDictionary<int, string>? value) { ScheduleSettingsSave(); SchedulePlaybackConfigRefresh(); }
    partial void OnTargetProcessNameChanged(string? value) => ScheduleSettingsSave();
    partial void OnNoteRangeLowChanged(int value)
    {
        if (value > NoteRangeHigh) NoteRangeHigh = value;
        InvalidateProfileSelection();
        if (!_applyingConfig) Profiles.RefreshKeyMappings(CreateConfig());
        ScheduleSettingsSave();
        SchedulePlaybackConfigRefresh();
    }
    partial void OnNoteRangeHighChanged(int value)
    {
        if (value < NoteRangeLow) NoteRangeLow = value;
        InvalidateProfileSelection();
        if (!_applyingConfig) Profiles.RefreshKeyMappings(CreateConfig());
        ScheduleSettingsSave();
        SchedulePlaybackConfigRefresh();
    }
    partial void OnWhiteKeyRatioChanged(double? value) => OnPropertyChanged(nameof(FitRatioLabel));
    partial void OnOriginalWhiteKeyRatioChanged(double? value) => OnPropertyChanged(nameof(OriginalFitRatioLabel));

    partial void OnInstrumentChanging(InstrumentKind oldValue, InstrumentKind newValue)
    {
        if (_profileApplied) return;
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
            _profileApplied = false;
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
            RebuildKeyboardPreview(CreateConfig());
            Profiles.ClearSelectedProfile();
            Profiles.RefreshKeyMappings(CreateConfig());
        }
        ScheduleSettingsSave();
    }

    private void InvalidateProfileSelection()
    {
        if (_applyingConfig || !_profileApplied) return;
        _profileApplied = false;
        Profiles.ClearSelectedProfile();
        Profiles.RefreshKeyMappings(CreateConfig());
    }

    partial void OnSelectedInstrumentChoiceChanged(InstrumentChoice? value)
    {
        if (_syncingInstrumentChoice || value is null) return;
        if (value.Profile is { } profile)
        {
            ApplyGameProfile(profile);
        }
        else
        {
            ApplyGenericInstrument(value.Kind);
        }
    }

    private void ApplyGameProfile(GameProfile profile)
    {
        if (Profiles.SelectedProfile?.Id != profile.Id)
        {
            Profiles.SelectProfile(profile);
        }
        _profileApplied = true;
        ApplyConfig(profile.Config);
        Profiles.RefreshKeyMappings(profile.Config);
        StatusText = $"已应用：{profile.Name}";
        ScheduleSettingsSave();
    }

    private void ApplyGenericInstrument(InstrumentKind kind)
    {
        Profiles.ClearSelectedProfile();
        if (Instrument != kind)
        {
            Instrument = kind;
            _profileApplied = false;
        }
        else
        {
            _profileApplied = false;
            CustomKeyMap = GetSavedKeyMap(kind);
            var range = GetSavedRange(kind);
            NoteRangeLow = range.Low;
            NoteRangeHigh = range.High;
            if (_midi is not null)
            {
                _playback.Load(_midi, CreateConfig());
                EventCount = PlaybackEventBuilder.Build(_midi.Notes, CreateConfig()).Count;
                OnPropertyChanged(nameof(EventCountLabel));
                UpdateFitRatios();
            }
            RebuildKeyboardPreview(CreateConfig());
            Profiles.RefreshKeyMappings(CreateConfig());
        }
        SyncSelectedInstrumentChoice();
        ScheduleSettingsSave();
    }

    private void OnGameProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            for (var index = InstrumentChoices.Count - 1; index >= 0; index--)
            {
                if (!InstrumentChoices[index].IsGeneric) InstrumentChoices.RemoveAt(index);
            }
        }
        else if (e.NewItems is not null)
        {
            foreach (GameProfile profile in e.NewItems)
            {
                InstrumentChoices.Add(new InstrumentChoice(profile.Config.Instrument, profile));
            }
        }
        else if (e.OldItems is not null)
        {
            var removed = e.OldItems.Cast<GameProfile>().Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var index = InstrumentChoices.Count - 1; index >= 0; index--)
            {
                var choice = InstrumentChoices[index];
                if (!choice.IsGeneric && removed.Contains(choice.Profile!.Id)) InstrumentChoices.RemoveAt(index);
            }
        }
        SyncSelectedInstrumentChoice();
    }

    private void OnProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfilesViewModel.SelectedProfile))
        {
            SyncSelectedInstrumentChoice();
        }
    }

    private void SyncSelectedInstrumentChoice()
    {
        _syncingInstrumentChoice = true;
        try
        {
            SelectedInstrumentChoice = Profiles.SelectedProfile is { } profile
                ? InstrumentChoices.FirstOrDefault(choice => choice.Profile?.Id == profile.Id) ?? FindGenericChoice(Instrument)
                : FindGenericChoice(Instrument);
        }
        finally
        {
            _syncingInstrumentChoice = false;
        }
    }

    private InstrumentChoice? FindGenericChoice(InstrumentKind kind) =>
        InstrumentChoices.FirstOrDefault(choice => choice.IsGeneric && choice.Kind == kind)
        ?? InstrumentChoices.FirstOrDefault(choice => choice.IsGeneric);

    private IReadOnlyDictionary<int, string>? GetSavedKeyMap(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Piano => _pianoKeyMap,
        InstrumentKind.Drums => _drumKeyMap,
        InstrumentKind.Microphone => _microphoneKeyMap,
        _ => null,
    };

    private (int Low, int High) GetSavedRange(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Piano => _pianoRange,
        InstrumentKind.Drums => _drumRange,
        InstrumentKind.Microphone => _microphoneRange,
        _ => _pianoRange,
    };

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
            if (!_timelineDragging) SeekPosition = snapshot.CursorMs;
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

    private void RebuildKeyboardPreview(MappingConfig config)
    {
        var active = KeyboardKeys.Where(key => key.IsActive).Select(key => key.Label).ToHashSet(StringComparer.Ordinal);
        var labels = config.Instrument switch
        {
            InstrumentKind.Drums => DistinctPreviewKeys(config.CustomKeyMap, NoteMapping.DrumKeys),
            InstrumentKind.Microphone => DistinctPreviewKeys(config.CustomKeyMap, MicrophoneDefaults),
            _ => DistinctPreviewKeys(config.CustomKeyMap, NoteMapping.PianoKeys),
        };

        KeyboardKeys.Clear();
        foreach (var label in labels)
        {
            KeyboardKeys.Add(new KeyboardKeyState(label) { IsActive = active.Contains(label) });
        }

        KeyboardCountText = $"{KeyboardKeys.Count} 键";
        KeyboardColumns = KeyboardKeys.Count switch
        {
            <= 10 => 5,
            <= 15 => 5,
            <= 21 => 7,
            <= 36 => 9,
            _ => 10,
        };
        KeyboardRows = (KeyboardKeys.Count + KeyboardColumns - 1) / KeyboardColumns;
    }

    private static IReadOnlyDictionary<int, string> MicrophoneDefaults { get; } =
        Enumerable.Range(NoteMapping.MicrophoneMinMidi, NoteMapping.MicrophoneKeys.Count)
            .ToDictionary(note => note, note => NoteMapping.MicrophoneKeys[note - NoteMapping.MicrophoneMinMidi]);

    private static IReadOnlyList<string> DistinctPreviewKeys(IReadOnlyDictionary<int, string>? custom, IReadOnlyDictionary<int, string> defaults)
    {
        var map = custom ?? defaults;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var pair in map.OrderBy(pair => pair.Key))
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && seen.Add(pair.Value))
            {
                result.Add(pair.Value);
            }
        }
        return result;
    }
}

public partial class KeyboardKeyState(string label) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] private bool isActive;
}
