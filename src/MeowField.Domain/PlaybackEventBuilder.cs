namespace MeowField.Domain;

public static class PlaybackEventBuilder
{
    // Keep short notes distinct while leaving enough time for Windows/game input queues.
    internal const int MinimumKeyHoldMs = 8;

    private readonly record struct MappedNote(string Key, int Velocity, int Start, int End);

    public static IReadOnlyList<PlayEvent> Build(IReadOnlyList<MidiNote> notes, MappingConfig config)
    {
        config.Validate();
        if (notes.Count == 0)
        {
            return [];
        }

        var octaveOffsets = config.Instrument != InstrumentKind.Drums && config.CollisionStrategy == CollisionStrategy.SmartOctaveFold
            ? NoteMapping.BuildOctaveOffsets(notes, config)
            : null;

        return config.Instrument switch
        {
            InstrumentKind.Drums => BuildDrumEvents(notes, config),
            InstrumentKind.Microphone => BuildClusteredEvents(notes, config, microphone: true, octaveOffsets),
            _ => BuildClusteredEvents(notes, config, microphone: false, octaveOffsets),
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
        bool microphone,
        IReadOnlyDictionary<int, int>? octaveOffsets)
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

            string? chordKey = null;
            if (!microphone && config.ChordMode is ChordMode.Prefer or ChordMode.Smart)
            {
                var normalized = ordered.Select(note => Normalize(note.Note, config, octaveOffsets)).ToArray();
                var detection = ChordDetector.DetectWithMembers(normalized);
                chordKey = detection?.Key;
                if (detection is not null)
                {
                    events.Add(new PlayEvent(ordered.Min(note => note.StartMs), PlayEventType.Down, detection.Value.Key, "chord"));
                    events.Add(new PlayEvent(ordered.Max(note => note.EndMs), PlayEventType.Up, detection.Value.Key, "chord"));

                    if (config.ChordMode == ChordMode.Prefer)
                    {
                        // Consume one source note for each chord tone. Extra
                        // melody notes in the same time cluster remain playable.
                        var remainingChordTones = detection.Value.PitchClasses.ToHashSet();
                        ordered = ordered
                            .Where((note, index) =>
                            {
                                var pitchClass = ((normalized[index] % 12) + 12) % 12;
                                return !remainingChordTones.Remove(pitchClass);
                            })
                            .ToArray();
                    }
                }
            }

            // If every note in the cluster was consumed by the chord, no melody
            // events remain. Extra notes continue through the normal mapping path.
            if (!microphone && config.ChordMode == ChordMode.Prefer && chordKey is not null && ordered.Length == 0)
            {
                continue;
            }

            var perNoteOffsets = config.CollisionStrategy == CollisionStrategy.PerNoteMinimal
                ? NoteMapping.ResolveClusterOctaves(ordered, config)
                : ordered.Select(note => octaveOffsets?.GetValueOrDefault(note.Note) ?? 0).ToArray();
            var mapped = ordered
                .Select((note, index) => (Note: note, Key: microphone
                    ? NoteMapping.MapMicrophoneKey(note.Note + perNoteOffsets[index] * 12, config.TransposeSemitones, config.CustomKeyMap, config.NoteRangeLow, config.NoteRangeHigh)
                    : NoteMapping.MapPianoKey(Normalize(note.Note, config, perNoteOffsets[index]), config.CustomKeyMap)))
                .Where(entry => entry.Key is not null)
                .Select(entry => new MappedNote(entry.Key!, entry.Note.Velocity, entry.Note.StartMs, entry.Note.EndMs))
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                // Max polyphony limits simultaneous keys, not repeated notes on one key.
                .OrderByDescending(entry => entry.Max(item => item.Velocity))
                .ThenBy(entry => entry.Min(item => item.Start))
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(Math.Max(1, config.MaxPolyphony))
                .SelectMany(SplitOverlappingNotes)
                .OrderByDescending(entry => entry.Velocity)
                .ThenBy(entry => entry.Start)
                .ThenBy(entry => entry.End)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal);

            foreach (var entry in mapped)
            {
                events.Add(new PlayEvent(entry.Start, PlayEventType.Down, entry.Key, microphone ? "mic" : "melody"));
                events.Add(new PlayEvent(entry.End, PlayEventType.Up, entry.Key, microphone ? "mic" : "melody"));
            }
        }

        return Sort(events);
    }

    private static IEnumerable<MappedNote> SplitOverlappingNotes(IEnumerable<MappedNote> notes)
    {
        var ordered = notes.OrderBy(note => note.Start).ThenBy(note => note.End).ToArray();
        if (ordered.Length == 0)
        {
            yield break;
        }

        var current = ordered[0];
        foreach (var next in ordered.Skip(1))
        {
            // A repeated mapped key must be released when the next note arrives.
            // Notes sharing the exact start cannot be distinguished on the target
            // keyboard, so retain the stronger/longer one as a single press.
            if (next.Start == current.Start)
            {
                current = current with
                {
                    Velocity = Math.Max(current.Velocity, next.Velocity),
                    End = Math.Max(current.End, next.End),
                };
                continue;
            }

            if (next.Start < current.End)
            {
                current = current with { End = next.Start };
            }

            yield return current;
            current = next;
        }

        yield return current;
    }

    private static Func<MidiNote, MidiNote> Scale(double speed) => note =>
    {
        var start = (int)(note.StartMs / speed);
        var end = (int)(note.EndMs / speed);
        return note with { StartMs = start, EndMs = Math.Max(start + MinimumKeyHoldMs, end) };
    };

    private static int Normalize(int note, MappingConfig config, IReadOnlyDictionary<int, int>? octaveOffsets = null)
    {
        var shifted = note + config.TransposeSemitones + (octaveOffsets?.GetValueOrDefault(note) ?? 0) * 12;
        var normalized = NoteMapping.FoldToRange(shifted, config.NoteRangeLow, config.NoteRangeHigh);
        return config.PreferNearestWhite
            ? NoteMapping.NearestWhite(normalized, config.NoteRangeLow, config.NoteRangeHigh)
            : normalized;
    }

    private static int Normalize(int note, MappingConfig config, int octaveOffset)
    {
        var shifted = note + config.TransposeSemitones + octaveOffset * 12;
        var normalized = NoteMapping.FoldToRange(shifted, config.NoteRangeLow, config.NoteRangeHigh);
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
        .ThenBy(item => item.Type == PlayEventType.Up ? 0 : 1)
        .ThenBy(item => item.Key, StringComparer.Ordinal)
        .ToArray();
}
