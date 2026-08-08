using MeowField.Domain;

namespace MeowField.App;

public partial class MainViewModel
{
    private async Task RestoreAsync()
    {
        _restoring = true;
        try
        {
            var migration = await _legacyImporter.ImportIfNeededAsync();
            var settings = await _store.LoadSettingsAsync();
            IsEnglish = settings.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            IsDarkTheme = settings.Theme.Equals("dark", StringComparison.OrdinalIgnoreCase);
            TargetProcessName = settings.BoundProcessName;
            RefreshWindows();
            _pianoKeyMap = settings.PianoKeyMap;
            _drumKeyMap = settings.DrumKeyMap;
            _microphoneKeyMap = settings.MicrophoneKeyMap;
            ApplyConfig(settings.Mapping);
            AutoTranspose = settings.Mapping.AutoTranspose;
            AutoPlayNext = settings.AutoPlayNext;
            Profiles.SelectProfileById(settings.SelectedGameProfileId);
            await Converter.RestoreExecutablePathAsync(settings.PianoTransPath);
            Queue.Clear();
            foreach (var item in await _store.LoadPlaylistAsync()) Queue.Add(item);
            SelectedQueueItem = Queue.FirstOrDefault();
            RaiseQueueState();
            if (migration.Imported) StatusText = migration.Message;
        }
        catch (Exception exception)
        {
            StatusText = $"设置恢复失败：{exception.Message}";
        }
        finally
        {
            _restoring = false;
        }
    }

    public async Task SaveAsync()
    {
        _saveCancellation?.Cancel();
        await _saveGate.WaitAsync();
        try
        {
            await _store.SaveSettingsAsync(new UserSettings
            {
                Locale = IsEnglish ? "en-US" : "zh-CN",
                Theme = IsDarkTheme ? "dark" : "light",
                Mapping = CreateConfig(),
                PianoKeyMap = Instrument == InstrumentKind.Piano ? CustomKeyMap : _pianoKeyMap,
                DrumKeyMap = Instrument == InstrumentKind.Drums ? CustomKeyMap : _drumKeyMap,
                MicrophoneKeyMap = Instrument == InstrumentKind.Microphone ? CustomKeyMap : _microphoneKeyMap,
                PianoTransPath = Converter.ConfiguredExecutablePath,
                AutoPlayNext = AutoPlayNext,
                SelectedGameProfileId = Profiles.SelectedProfile?.Id,
                BoundProcessName = SelectedWindow?.ProcessName ?? TargetProcessName,
            });
            await _store.SavePlaylistAsync(Queue);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ScheduleSettingsSave()
    {
        if (_restoring) return;
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = new CancellationTokenSource();
        _ = DebouncedSaveAsync(_saveCancellation.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer setting change superseded this write.
        }
    }
}
