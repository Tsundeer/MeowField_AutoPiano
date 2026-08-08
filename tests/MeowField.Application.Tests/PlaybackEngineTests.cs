using System.Collections.Concurrent;
using System.Diagnostics;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Application.Tests;

public sealed class PlaybackEngineTests
{
    [Fact]
    public void Stop_ReleasesEveryActiveKey()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi([new MidiNote(0, 10_000, 60, 100, 0, 0)], 10_000);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Any(item => item.Type == PlayEventType.Down), 1000));
        engine.Stop();

        Assert.Contains("Q", sink.Released);
        Assert.Equal(PlaybackState.Stopped, engine.Snapshot.State);
        Assert.Equal(0, engine.Snapshot.CursorMs);
    }

    [Fact]
    public void EventsWithSameTimestamp_AreSentAsOneBatch()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi(
            [new MidiNote(0, 5, 60, 100, 0, 0), new MidiNote(0, 5, 62, 100, 0, 0)],
            5);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => engine.Snapshot.State == PlaybackState.Stopped, 1000));

        Assert.Contains(sink.Batches, batch => batch.Count == 2 && batch.All(item => item.Type == PlayEventType.Down));
    }

    [Fact]
    public void Resume_RepressesAReleasedSustainedKey()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi([new MidiNote(0, 500, 60, 100, 0, 0)], 500);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Count(item => item.Type == PlayEventType.Down) == 1, 1000));
        engine.Pause();
        Assert.Contains("Q", sink.Released);

        engine.Play(123);

        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Count(item => item.Type == PlayEventType.Down) == 2, 1000));
        Assert.Contains("Q", engine.Snapshot.ActiveKeys);
        engine.Stop();
    }

    [Fact]
    public void Pause_DoesNotSendEventsWhileWaiting()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi([new MidiNote(0, 120, 60, 100, 0, 0)], 120);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Count == 1, 1000));
        engine.Pause();
        Thread.Sleep(180);

        Assert.Single(sink.Sent);
        Assert.Equal(PlaybackState.Paused, engine.Snapshot.State);

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Count == 3, 1000));
        Assert.Equal(2, sink.Sent.Count(item => item.Type == PlayEventType.Down));
        Assert.Single(sink.Sent, item => item.Type == PlayEventType.Up);
    }

    [Fact]
    public void PositiveLatency_AdvancesTheWholeTimelineWithoutPerBatchDelay()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi(
            [new MidiNote(200, 240, 60, 100, 0, 0), new MidiNote(300, 340, 62, 100, 0, 0)],
            340);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off, LinkLatencyMs = 150 });
        var started = Stopwatch.GetTimestamp();

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Count(item => item.Type == PlayEventType.Down) == 2, 1000));
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed.TotalMilliseconds, 100, 260);
        Assert.All(sink.Latencies, value => Assert.Equal(0, value));
    }

    [Fact]
    public void NegativeLatency_DelaysTheWholeTimeline()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi([new MidiNote(20, 30, 60, 100, 0, 0)], 30);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off, LinkLatencyMs = -100 });
        var started = Stopwatch.GetTimestamp();

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => sink.Sent.Any(item => item.Type == PlayEventType.Down), 1000));
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.InRange(elapsed.TotalMilliseconds, 90, 260);
    }

    [Fact]
    public void OverlappingNotesMappedToSameKey_ReleaseOnlyAfterLastNoteEnds()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var map = new Dictionary<int, string> { [60] = "Q", [62] = "Q" };
        var midi = new ParsedMidi([new MidiNote(0, 80, 60, 100, 0, 0), new MidiNote(20, 140, 62, 100, 0, 0)], 140);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off, CustomKeyMap = map });

        engine.Play(123);
        Assert.True(SpinWait.SpinUntil(() => engine.Snapshot.State == PlaybackState.Stopped, 1000));

        Assert.Single(sink.Sent, item => item.Type == PlayEventType.Down);
        Assert.Single(sink.Sent, item => item.Type == PlayEventType.Up);
    }

    [Fact]
    public void LongGap_PublishesProgressBetweenMidiEvents()
    {
        var sink = new RecordingInputSink();
        using var engine = new PlaybackEngine(sink);
        var midi = new ParsedMidi([new MidiNote(0, 300, 60, 100, 0, 0)], 300);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);

        Assert.True(SpinWait.SpinUntil(() => engine.Snapshot.CursorMs >= 100, 1000));
        Assert.Equal(PlaybackState.Playing, engine.Snapshot.State);
        Assert.InRange(engine.Snapshot.Progress, 20, 80);
        engine.Stop();
    }

    [Fact]
    public void InputFailure_TransitionsToFaultedWithoutEscapingCleanup()
    {
        using var engine = new PlaybackEngine(new FailingInputSink());
        var midi = new ParsedMidi([new MidiNote(0, 200, 60, 100, 0, 0)], 200);
        engine.Load(midi, new MappingConfig { ChordMode = ChordMode.Off });

        engine.Play(123);

        Assert.True(SpinWait.SpinUntil(() => engine.Snapshot.State == PlaybackState.Faulted, 1000));
        Assert.Contains("target lost focus", engine.Snapshot.Error);
    }

    private sealed class RecordingInputSink : IInputSink
    {
        public ConcurrentBag<PlayEvent> Sent { get; } = [];
        public ConcurrentBag<string> Released { get; } = [];
        public ConcurrentBag<IReadOnlyList<PlayEvent>> Batches { get; } = [];
        public ConcurrentBag<int> Latencies { get; } = [];

        public void SendBatch(IReadOnlyList<PlayEvent> events, InputMode mode, nint targetWindow, int latencyMs)
        {
            Batches.Add(events);
            Latencies.Add(latencyMs);
            foreach (var item in events)
            {
                Sent.Add(item);
            }
        }

        public void ReleaseAll(IEnumerable<string> keys, InputMode mode, nint targetWindow)
        {
            foreach (var key in keys)
            {
                Released.Add(key);
            }
        }
    }

    private sealed class FailingInputSink : IInputSink
    {
        public void SendBatch(IReadOnlyList<PlayEvent> events, InputMode mode, nint targetWindow, int latencyMs) =>
            throw new InvalidOperationException("target lost focus");

        public void ReleaseAll(IEnumerable<string> keys, InputMode mode, nint targetWindow) =>
            throw new InvalidOperationException("target lost focus");
    }
}
