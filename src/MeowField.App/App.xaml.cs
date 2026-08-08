using System.Windows;
using System.IO;
using MeowField.Application;
using MeowField.Infrastructure.Midi;
using MeowField.Infrastructure.Audio;
using MeowField.Infrastructure.Storage;
using MeowField.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows.Threading;

namespace MeowField.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeowField", "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDirectory, "app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true)
            .CreateLogger();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IMidiFileReader, DryWetMidiFileReader>();
                    services.AddSingleton<IWindowCatalog, WindowsWindowCatalog>();
                    services.AddSingleton<IInputSink, WindowsInputSink>();
                    services.AddSingleton<IPlaybackEngine, PlaybackEngine>();
                    services.AddSingleton<IUserDataStore, FileSystemUserDataStore>();
                    services.AddSingleton<IGameProfileProvider>(_ => new GameProfileProvider(Path.Combine(AppContext.BaseDirectory, "game_configs")));
                    services.AddSingleton<ILibraryService, MidiLibraryService>();
                    services.AddSingleton<IAudioConverter, PianoTransAudioConverter>();
                    services.AddSingleton<INtpService, NtpService>();
                    services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
                    services.AddSingleton<IScheduledPlaybackService, ScheduledPlaybackService>();
                    services.AddSingleton<IDiagnosticService, DiagnosticService>();
                    services.AddSingleton<ILegacyDataImporter, LegacyDataImporter>();
                    services.AddSingleton<LibraryViewModel>();
                    services.AddSingleton<ProfilesViewModel>();
                    services.AddSingleton<ConverterViewModel>();
                    services.AddSingleton<ScheduleViewModel>();
                    services.AddSingleton<DiagnosticsViewModel>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.Start();
            _host.Services.GetRequiredService<MainWindow>().Show();
            var midiArgument = e.Args.FirstOrDefault(path =>
                File.Exists(path) && (Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase)));
            if (midiArgument is not null)
            {
                _ = _host.Services.GetRequiredService<MainViewModel>().LoadMidiAsync(midiArgument);
            }
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application startup failed");
            Log.CloseAndFlush();
            MessageBox.Show(
                $"MeowField 启动失败。\n\n{exception.Message}\n\n日志目录：{logDirectory}",
                "MeowField AutoPlay Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            $"程序遇到未处理错误：{e.Exception.Message}\n\n请在“日志与诊断”中导出诊断包。",
            "MeowField AutoPlay Lite",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Services.GetService<MainViewModel>()?.SaveAsync().GetAwaiter().GetResult();
            _host?.Services.GetService<IGlobalHotkeyService>()?.Unregister();
            _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to persist settings during shutdown");
        }
        _host?.Services.GetService<IPlaybackEngine>()?.Dispose();
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
