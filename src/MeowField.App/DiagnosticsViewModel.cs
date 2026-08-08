using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowField.Application;
using Microsoft.Win32;

namespace MeowField.App;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticService _diagnostic;
    private readonly IWindowCatalog _windows;

    public DiagnosticsViewModel(IDiagnosticService diagnostic, IWindowCatalog windows)
    {
        _diagnostic = diagnostic;
        _windows = windows;
        IsAdministrator = windows.IsAdministrator;
    }

    [ObservableProperty] private string statusText = "诊断信息就绪";
    [ObservableProperty] private bool isAdministrator;
    [ObservableProperty] private bool isBusy;

    public string LogDirectory => _diagnostic.LogDirectory;
    public string RuntimeText => $"{Environment.OSVersion.VersionString} · {Environment.Is64BitProcess switch { true => "x64", false => "x86" }} · .NET {Environment.Version}";

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Filter = "诊断包 (*.zip)|*.zip|日志文本 (*.log)|*.log", FileName = "meowfield-diagnostics.zip" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _diagnostic.ExportAsync(dialog.FileName);
            StatusText = $"已导出：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            StatusText = $"导出失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
