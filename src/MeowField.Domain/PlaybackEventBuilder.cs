namespace MeowField.Domain;

public static class PlaybackEventBuilder
{
    public static IReadOnlyList<PlayEvent> Build(IReadOnlyList<MidiNote> notes, MappingConfig config)
    {
        config.Validate();
        if (notes.Count == 0)
        {
            return [];
        }

        return config.Instrument switch
        {
            InstrumentKind.Drums => BuildDrumEvents(notes, config),
            InstrumentKind.Microphone => BuildClusteredEvents(notes, config, microphone: true),
            _ => BuildClusteredEvents(notes, config, microphone: false),
        };
    }

    private static IReadOnlyList<PlayEvent> BuildDrumEvents(IReadOnlyList<MidiNote> notes, MappingConfig config)
    {
        var events = notes
            .Where(note => config.MidiChannelFilter is null || note.Channel == config.MidiChannelFilter)
            .Select(Scale(config.Speed))
            .SelectMany(note =>
            {
                var key = (config.CustomKeyMap ?? NoteMapping.DrumKeys).GetValueOrDefault(note.Note);
                return key is null
                    ? []
                    : new[]
                    {
                        new PlayEvent(note.StartMs, PlayEventType.Down, key, "drum", note.Note),
                        new PlayEvent(note.EndMs, PlayEventType.Up, key, "drum", note.Note),
                    };
            });

        return Sort(events);
    }

    private static IReadOnlyList<PlayEvent> BuildClusteredEvents(
        IReadOnlyList<MidiNote> notes,
        MappingConfig config,
        bool microphone)
    {
        var scaled = notes.Select(Scale(config.Speed))
            .OrderBy(note => note.StartMs)
            .ThenBy(note => note.EndMs)
            .ThenByDescending(note => note.Note)
            .ToArray();

        var events = new List<PlayEvent>();
        foreach (var group in Cluster(scaled, config.ChordClusterWindowMs))
        {
            var ordered = group.OrderByDescending(note => note.Note)
                .ThenByDescending(note => note.Velocity)
                .ThenBy(note => note.EndMs)
                .ToArray();

            if (!microphone && config.ChordPrefer)
            {
                var normalized = ordered.Select(note => Normalize(note.Note, config)).ToArray();
                var chordKey = ChordDetector.Detect(normalized);
                if (chordKey is not null)
                {
                    events.Add(new PlayEvent(ordered.Min(note => note.StartMs), PlayEventType.Down, chordKey, "chord"));
                    events.Add(new PlayEvent(ordered.Max(note => note.EndMs), PlayEventType.Up, chordKey, "chord"));

                    if (config.KeepMelodyTopNote)
                    {
                        var melody = ordered[0];
                        var normalizedMelody = Normalize(melody.Note, config);
                        var melodyKey = NoteMapping.MapPianoKey(normalizedMelody, config.CustomKeyMap);
                        if (melodyKey is not null)
                        {
                            events.Add(new PlayEvent(melody.StartMs, PlayEventType.Down, melodyKey, "melody", normalizedMelody));
                            events.Add(new PlayEvent(melody.EndMs, PlayEventType.Up, melodyKey, "melody", normalizedMelody));
                        }
                    }

                    continue;
                }
            }

            var mapped = ordered
                .Select(note => (Note: note, Key: microphone
                    ? NoteMapping.MapMicrophoneKey(note.Note, config.TransposeSemitones, config.CustomKeyMap, config.NoteRangeLow, config.NoteRangeHigh)
                    : NoteMapping.MapPianoKey(Normalize(note.Note, config), config.CustomKeyMap)))
                .Where(entry => entry.Key is not null)
                .GroupBy(entry => entry.Key!, StringComparer.Ordinal)
                .Select(entry => new
                {
                    Key = entry.Key,
                    Velocity = entry.Max(item => item.Note.Velocity),
                    Start = entry.Min(item => item.Note.StartMs),
                    End = entry.Max(item => item.Note.EndMs),
                })
                .OrderByDescending(entry => entry.Velocity)
                .ThenBy(entry => entry.Start)
                .ThenBy(entry => entry.End)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(Math.Max(1, config.MaxPolyphony));

            foreach (var entry in mapped)
            {
                events.Add(new PlayEvent(entry.Start, PlayEventType.Down, entry.Key, microphone ? "mic" : "melody"));
                events.Add(new PlayEvent(entry.End, PlayEventType.Up, entry.Key, microphone ? "mic" : "melody"));
            }
        }

        return Sort(events);
    }

    private static Func<MidiNote, MidiNote> Scale(double speed) => note =>
    {
        var start = (int)(note.StartMs / speed);
        var end = (int)(note.EndMs / speed);
        return note with { StartMs = start, EndMs = Math.Max(start + 1, end) };
    };

    private static int Normalize(int note, MappingConfig config)
    {
        var normalized = NoteMapping.FoldToRange(note + config.TransposeSemitones, config.NoteRangeLow, config.NoteRangeHigh);
        return config.PreferNearestWhite
            ? NoteMapping.NearestWhite(normalized, config.NoteRangeLow, config.NoteRangeHigh)
            : normalized;
    }

    private static IEnumerable<IReadOnlyList<MidiNote>> Cluster(IReadOnlyList<MidiNote> notes, int windowMs)
    {
        var current = new List<MidiNote>();
        var currentStart = 0;
        foreach (var note in notes)
        {
            if (current.Count == 0)
            {
                current.Add(note);
                currentStart = note.StartMs;
            }
            else if (note.StartMs - currentStart <= windowMs)
            {
                current.Add(note);
            }
            else
            {
                yield return current;
                current = [note];
                currentStart = note.StartMs;
            }
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static IReadOnlyList<PlayEvent> Sort(IEnumerable<PlayEvent> events) => events
        .OrderBy(item => item.TimeMs)
        .ThenBy(item => item.Type == PlayEventType.Down ? 0 : 1)
        .ThenBy(item => item.Key, StringComparer.Ordinal)
        .ToArray();
}
