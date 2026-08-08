using System.Diagnostics;
using MeowField.Domain;

namespace MeowField.Application;

public sealed class PlaybackEngine : IPlaybackEngine
{
    private readonly IInputSink _input;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _resumeSignal = new(initialState: true);
    private IReadOnlyList<PlayEvent> _events = [];
    private MappingConfig _config = new();
    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private HashSet<string> _activeKeys = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _keyDepths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _pausedKeys = new(StringComparer.OrdinalIgnoreCase);
    private PlaybackSnapshot _snapshot = new(PlaybackState.Idle, 0, 0, new HashSet<string>());
    private nint _targetWindow;
    private bool _disposed;

    public PlaybackEngine(IInputSink input)
    {
        _input = input;
    }

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Load(ParsedMidi midi, MappingConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopWorker(resetCursor: true);
        var events = PlaybackEventBuilder.Build(midi.Notes, config);
        var scaledDuration = config.Speed > 0 ? (int)(midi.DurationMs / config.Speed) : midi.DurationMs;

        lock (_gate)
        {
            _events = events;
            _config = config;
            _activeKeys = new(StringComparer.OrdinalIgnoreCase);
            _keyDepths = new(StringComparer.OrdinalIgnoreCase);
            _pausedKeys = new(StringComparer.OrdinalIgnoreCase);
            SetSnapshotLocked(new PlaybackSnapshot(PlaybackState.Loaded, 0, scaledDuration, SnapshotKeys()));
        }
    }

    public void Play(nint targetWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_snapshot.State == PlaybackState.Playing || _events.Count == 0)
            {
                return;
            }

            _targetWindow = targetWindow;
            if (_snapshot.State == PlaybackState.Paused && _thread is { IsAlive: true })
            {
                if (_pausedKeys.Count > 0)
                {
                    var resumeEvents = _pausedKeys
                        .Select(key => new PlayEvent(_snapshot.CursorMs, PlayEventType.Down, key, "resume"))
                        .ToArray();
                    _input.SendBatch(resumeEvents, _config.InputMode, _targetWindow, latencyMs: 0);
                    _activeKeys.UnionWith(_pausedKeys);
                    _pausedKeys.Clear();
                }
                _resumeSignal.Set();
                SetSnapshotLocked(_snapshot with { State = PlaybackState.Playing, ActiveKeys = SnapshotKeys() });
                return;
            }

            _cancellation = new CancellationTokenSource();
            _resumeSignal.Set();
            var startCursor = _snapshot.CursorMs;
            SetSnapshotLocked(_snapshot with { State = PlaybackState.Playing, Error = null });
            _thread = new Thread(() => Run(startCursor, _cancellation.Token))
            {
                IsBackground = true,
                Name = "MeowField.Playback",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_snapshot.State != PlaybackState.Playing)
            {
                return;
            }

            _resumeSignal.Reset();
            _pausedKeys = new HashSet<string>(_activeKeys, StringComparer.OrdinalIgnoreCase);
            SetSnapshotLocked(_snapshot with { State = PlaybackState.Paused });
            ReleaseActiveKeysLocked();
        }
    }

    public void Seek(int cursorMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool wasPlaying;
        bool wasPaused;
        nint targetWindow;
        lock (_gate)
        {
            if (_events.Count == 0) return;
            wasPlaying = _snapshot.State == PlaybackState.Playing;
            wasPaused = _snapshot.State == PlaybackState.Paused;
            targetWindow = _targetWindow;
        }

        StopWorker(resetCursor: false);
        lock (_gate)
        {
            var position = Math.Clamp(cursorMs, 0, _snapshot.DurationMs);
            var state = wasPlaying ? PlaybackState.Loaded : wasPaused ? PlaybackState.Paused : _snapshot.State;
            SetSnapshotLocked(_snapshot with { State = state, CursorMs = position, ActiveKeys = SnapshotKeys() });
        }

        if (wasPlaying)
        {
            Play(targetWindow);
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopWorker(resetCursor: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWorker(resetCursor: true);
        _resumeSignal.Dispose();
        _disposed = true;
    }

    private void Run(int startCursorMs, CancellationToken cancellationToken)
    {
        try
        {
            var frequency = Stopwatch.Frequency;
            var origin = Stopwatch.GetTimestamp() - (long)(startCursorMs / 1000d * frequency);
            var index = 0;
            while (index < _events.Count && _events[index].TimeMs < startCursorMs)
            {
                index++;
            }

            var lastSnapshotAt = Stopwatch.GetTimestamp();
            while (index < _events.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_resumeSignal.IsSet)
                {
                    var pausedAt = Stopwatch.GetTimestamp();
                    _resumeSignal.Wait(cancellationToken);
                    origin += Stopwatch.GetTimestamp() - pausedAt;
                }

                var eventTime = _events[index].TimeMs;
                var compensatedTimeMs = Math.Max(0, eventTime - _config.LinkLatencyMs);
                WaitUntil(ref origin, (long)(compensatedTimeMs / 1000d * frequency), cancellationToken);

                var end = index + 1;
                while (end < _events.Count && _events[end].TimeMs == eventTime)
                {
                    end++;
                }

                var batch = new List<PlayEvent>();
                lock (_gate)
                {
                    foreach (var item in _events.Skip(index).Take(end - index))
                    {
                        if (item.Type == PlayEventType.Down)
                        {
                            var depth = _keyDepths.GetValueOrDefault(item.Key);
                            _keyDepths[item.Key] = depth + 1;
                            if (depth == 0)
                            {
                                _activeKeys.Add(item.Key);
                                batch.Add(item);
                            }
                        }
                        else if (_keyDepths.TryGetValue(item.Key, out var depth))
                        {
                            if (depth <= 1)
                            {
                                _keyDepths.Remove(item.Key);
                                _activeKeys.Remove(item.Key);
                                batch.Add(item);
                            }
                            else
                            {
                                _keyDepths[item.Key] = depth - 1;
                            }
                        }
                    }
                    var now = Stopwatch.GetTimestamp();
                    if (now - lastSnapshotAt >= frequency / 30 || end == _events.Count)
                    {
                        SetSnapshotLocked(_snapshot with { CursorMs = eventTime, ActiveKeys = SnapshotKeys() });
                        lastSnapshotAt = now;
                    }
                }

                _input.SendBatch(batch, _config.InputMode, _targetWindow, latencyMs: 0);

                index = end;
            }

            lock (_gate)
            {
                ReleaseActiveKeysLocked(clearDepths: true);
                SetSnapshotLocked(_snapshot with
                {
                    State = PlaybackState.Stopped,
                    CursorMs = _snapshot.DurationMs,
                    ActiveKeys = SnapshotKeys(),
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Stop and Load own the final state transition.
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                ReleaseActiveKeysLocked(clearDepths: true);
                SetSnapshotLocked(_snapshot with
                {
                    State = PlaybackState.Faulted,
                    Error = exception.Message,
                    ActiveKeys = SnapshotKeys(),
                });
            }
        }
    }

    private void WaitUntil(ref long origin, long eventOffset, CancellationToken cancellationToken)
    {
        var frequency = Stopwatch.Frequency;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_resumeSignal.IsSet)
            {
                var pausedAt = Stopwatch.GetTimestamp();
                _resumeSignal.Wait(cancellationToken);
                origin += Stopwatch.GetTimestamp() - pausedAt;
                continue;
            }

            var dueTimestamp = origin + eventOffset;
            var remaining = dueTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            var remainingMs = remaining * 1000d / frequency;
            if (remainingMs > 3)
            {
                Thread.Sleep(Math.Min(5, Math.Max(1, (int)remainingMs - 1)));
                PublishProgress(origin, frequency);
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }

    private void StopWorker(bool resetCursor)
    {
        Thread? thread;
        lock (_gate)
        {
            _cancellation?.Cancel();
            _resumeSignal.Set();
            thread = _thread;
        }

        if (thread is { IsAlive: true } && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (_gate)
        {
            ReleaseActiveKeysLocked(clearDepths: true);
            _pausedKeys.Clear();
            _cancellation?.Dispose();
            _cancellation = null;
            _thread = null;
            var nextState = _events.Count == 0 ? PlaybackState.Idle : PlaybackState.Stopped;
            SetSnapshotLocked(_snapshot with
            {
                State = nextState,
                CursorMs = resetCursor ? 0 : _snapshot.CursorMs,
                ActiveKeys = SnapshotKeys(),
            });
        }
    }

    private void PublishProgress(long origin, long frequency)
    {
        var elapsed = (Stopwatch.GetTimestamp() - origin) * 1000L / frequency;
        var cursor = (int)Math.Clamp(elapsed + _config.LinkLatencyMs, 0, _snapshot.DurationMs);
        lock (_gate)
        {
            if (_snapshot.State == PlaybackState.Playing && cursor > _snapshot.CursorMs)
                SetSnapshotLocked(_snapshot with { CursorMs = cursor, ActiveKeys = SnapshotKeys() });
        }
    }

    private void ReleaseActiveKeysLocked(bool clearDepths = false)
    {
        if (_activeKeys.Count > 0)
        {
            try
            {
                _input.ReleaseAll(_activeKeys, _config.InputMode, _targetWindow);
            }
            catch
            {
                // The target can disappear or lose focus while stopping. Cleanup must never terminate the process.
            }
            _activeKeys.Clear();
        }
        if (clearDepths) _keyDepths.Clear();
    }

    private IReadOnlySet<string> SnapshotKeys() => new HashSet<string>(_activeKeys, StringComparer.OrdinalIgnoreCase);

    private void SetSnapshotLocked(PlaybackSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
