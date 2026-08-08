using MeowField.Domain;

namespace MeowField.Application;

public sealed class ScheduledPlaybackService : IScheduledPlaybackService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private ScheduledPlayback? _current;
    private bool _disposed;

    public event EventHandler<ScheduledPlayback>? Due;

    public ScheduledPlayback? Current
    {
        get { lock (_gate) return _current; }
    }

    public async Task ScheduleAsync(ScheduledPlayback schedule, NtpMeasurement? measurement = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Cancel();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _cancellation = linked;
            _current = schedule;
        }

        try
        {
            var target = schedule.LocalStart.ToUniversalTime();
            if (measurement?.Success == true)
            {
                target -= measurement.Offset;
            }

            if (schedule.LinkLatencyMs != 0)
            {
                target -= TimeSpan.FromMilliseconds(schedule.LinkLatencyMs);
            }

            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                var remaining = target - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining > TimeSpan.FromHours(12) ? TimeSpan.FromHours(12) : remaining, linked.Token);
            }

            Due?.Invoke(this, schedule);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cancellation, linked))
                {
                    _cancellation = null;
                    _current = null;
                }
            }
            linked.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            _cancellation = null;
            _current = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Cancel();
        _disposed = true;
    }
}
