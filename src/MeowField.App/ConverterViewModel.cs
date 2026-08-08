using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using MeowField.Domain;
using Microsoft.Win32;

namespace MeowField.App;

public partial class ConverterViewModel : ObservableObject
{
    private readonly IAudioConverter _converter;
    private CancellationTokenSource? _conversionCancellation;

    public ConverterViewModel(IAudioConverter converter)
    {
        _converter = converter;
        ExecutablePath = converter.ExecutablePath ?? "未找到 PianoTrans.exe";
        IsAvailable = converter.IsAvailable;
    }

    [ObservableProperty] private string inputPath = "";
    [ObservableProperty] private string outputPath = "";
    [ObservableProperty] private string executablePath;
    [ObservableProperty] private string statusText = "请选择音频文件";
    [ObservableProperty] private bool isAvailable;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progress;

    public event EventHandler<string>? MidiReady;
    public string? ConfiguredExecutablePath => _converter.ExecutablePath;

    public async Task RestoreExecutablePathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var result = await _converter.SetPathAsync(path);
        IsAvailable = _converter.IsAvailable;
        ExecutablePath = _converter.ExecutablePath ?? "未找到 PianoTrans.exe";
        if (!result.Success) StatusText = result.Message;
    }

    [RelayCommand]
    private void SelectAudio()
    {
        var dialog = new OpenFileDialog { Filter = "音频文件|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        InputPath = dialog.FileName;
        OutputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(InputPath)!, System.IO.Path.GetFileNameWithoutExtension(InputPath) + ".mid");
    }

    [RelayCommand]
    private async Task SelectConverterAsync()
    {
        var dialog = new OpenFolderDialog { Title = "选择 PianoTrans 目录" };
        if (dialog.ShowDialog() != true) return;
        var result = await _converter.SetPathAsync(dialog.FolderName);
        IsAvailable = _converter.IsAvailable;
        ExecutablePath = _converter.ExecutablePath ?? "未找到 PianoTrans.exe";
        StatusText = result.Message;
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (string.IsNullOrWhiteSpace(InputPath) || !System.IO.File.Exists(InputPath))
        {
            StatusText = "请选择有效的音频文件";
            return;
        }

        try
        {
            IsBusy = true;
            _conversionCancellation?.Dispose();
            _conversionCancellation = new CancellationTokenSource();
            Progress = 0;
            var progress = new Progress<ConversionProgress>(value =>
            {
                StatusText = value.Message;
                Progress = value.Status == "completed" ? 100 : Math.Min(95, value.Elapsed.TotalSeconds * 5);
            });
            var result = await _converter.ConvertAsync(InputPath, OutputPath, progress, _conversionCancellation.Token);
            StatusText = result.Message;
            if (result.Success && result.MidiPath is not null) MidiReady?.Invoke(this, result.MidiPath);
        }
        catch (Exception exception)
        {
            StatusText = $"转换失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelConversion() => _conversionCancellation?.Cancel();
}
