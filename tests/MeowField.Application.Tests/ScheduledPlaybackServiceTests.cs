using System.Diagnostics;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Application.Tests;

public sealed class ScheduledPlaybackServiceTests
{
    [Fact]
    public async Task Cancel_ClearsCurrentAndPreventsDueEvent()
    {
        using var service = new ScheduledPlaybackService();
        var dueCount = 0;
        service.Due += (_, _) => Interlocked.Increment(ref dueCount);
        var schedule = CreateSchedule(DateTimeOffset.Now.AddMilliseconds(250));

        var pending = service.ScheduleAsync(schedule);
        Assert.Same(schedule, service.Current);
        service.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(service.Current);
        await Task.Delay(300);
        Assert.Equal(0, dueCount);
    }

    [Fact]
    public async Task PositiveLatency_FiresBeforeLocalStart()
    {
        using var service = new ScheduledPlaybackService();
        var started = Stopwatch.GetTimestamp();
        var schedule = CreateSchedule(DateTimeOffset.Now.AddMilliseconds(300), latencyMs: 200);

        await service.ScheduleAsync(schedule);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed.TotalMilliseconds, 40, 240);
    }

    [Fact]
    public async Task SuccessfulNtpOffset_AdjustsDueTime()
    {
        using var service = new ScheduledPlaybackService();
        var started = Stopwatch.GetTimestamp();
        var schedule = CreateSchedule(DateTimeOffset.Now.AddMilliseconds(300));
        var measurement = new NtpMeasurement(true, "test", TimeSpan.FromMilliseconds(200), TimeSpan.Zero, DateTimeOffset.UtcNow);

        await service.ScheduleAsync(schedule, measurement);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed.TotalMilliseconds, 40, 240);
    }

    [Fact]
    public async Task NegativeLatency_FiresAfterLocalStart()
    {
        using var service = new ScheduledPlaybackService();
        var started = Stopwatch.GetTimestamp();
        var schedule = CreateSchedule(DateTimeOffset.Now.AddMilliseconds(80), latencyMs: -120);

        await service.ScheduleAsync(schedule);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed.TotalMilliseconds, 160, 350);
    }

    private static ScheduledPlayback CreateSchedule(DateTimeOffset start, int latencyMs = 0) =>
        new(Guid.NewGuid().ToString("N"), start, "test.mid", latencyMs, false);
}
