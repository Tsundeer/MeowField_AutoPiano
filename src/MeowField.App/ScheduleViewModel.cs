using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.App;

public partial class ScheduleViewModel : ObservableObject
{
    private readonly INtpService _ntp;
    private readonly IScheduledPlaybackService _scheduler;
    private Task? _scheduleTask;

    public ScheduleViewModel(INtpService ntp, IScheduledPlaybackService scheduler)
    {
        _ntp = ntp;
        _scheduler = scheduler;
        _scheduler.Due += (_, schedule) =>
        {
            OnPropertyChanged(nameof(IsScheduled));
            Due?.Invoke(this, schedule);
        };
    }

    [ObservableProperty] private DateTime scheduledDate = DateTime.Today;
    [ObservableProperty] private string scheduledTime = "20:00";
    [ObservableProperty] private bool useNtp = true;
    [ObservableProperty] private int linkLatencyMs;
    [ObservableProperty] private string midiPath = "";
    [ObservableProperty] private string serverText = "未测量";
    [ObservableProperty] private string statusText = "尚未设置定时任务";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private NtpMeasurement? measurement;

    public event EventHandler<ScheduledPlayback>? Due;
    public Func<string?>? MidiPathProvider { get; set; }

    public bool IsScheduled => _scheduler.Current is not null;

    [RelayCommand]
    private async Task SyncNtpAsync()
    {
        try
        {
            IsBusy = true;
            Measurement = await _ntp.MeasureAsync();
            ServerText = Measurement.Success
                ? $"{Measurement.Server} · RTT {Measurement.RoundTripTime.TotalMilliseconds:0.0} ms · 偏移 {Measurement.Offset.TotalMilliseconds:+0.0;-0.0;0} ms"
                : Measurement.Error ?? "同步失败";
            StatusText = Measurement.Success ? "NTP 测量完成" : "NTP 测量失败";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ScheduleAsync()
    {
        var path = string.IsNullOrWhiteSpace(MidiPath) ? MidiPathProvider?.Invoke() : MidiPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "请先载入有效 MIDI";
            return;
        }

        if (!TimeOnly.TryParse(ScheduledTime, out var time))
        {
            StatusText = "时间格式应为 HH:mm 或 HH:mm:ss";
            return;
        }

        var local = new DateTimeOffset(ScheduledDate.Date.Add(time.ToTimeSpan()), TimeZoneInfo.Local.GetUtcOffset(ScheduledDate));
        if (local <= DateTimeOffset.Now) local = local.AddDays(1);
        var schedule = new ScheduledPlayback(Guid.NewGuid().ToString("N"), local, path!, LinkLatencyMs, UseNtp);
        try
        {
            _scheduleTask = ObserveScheduleAsync(schedule, UseNtp ? Measurement : null);
            StatusText = $"已设置：{local:yyyy-MM-dd HH:mm:ss}";
            OnPropertyChanged(nameof(IsScheduled));
        }
        catch (Exception exception)
        {
            StatusText = $"定时设置失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private void CancelSchedule()
    {
        _scheduler.Cancel();
        StatusText = "定时任务已取消";
        OnPropertyChanged(nameof(IsScheduled));
    }

    private async Task ObserveScheduleAsync(ScheduledPlayback schedule, NtpMeasurement? measurement)
    {
        try
        {
            await _scheduler.ScheduleAsync(schedule, measurement);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when replacing or cancelling a schedule.
        }
        catch (Exception exception)
        {
            StatusText = $"定时任务失败：{exception.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(IsScheduled));
        }
    }
}
