namespace MeowField.Domain;

public static class NoteMapping
{
    private static readonly HashSet<int> WhitePitchClasses = [0, 2, 4, 5, 7, 9, 11];

    public static readonly IReadOnlyDictionary<int, string> PianoKeys = new Dictionary<int, string>
    {
        [48] = "A", [50] = "S", [52] = "D", [53] = "F", [55] = "G", [57] = "H", [59] = "J",
        [60] = "Q", [62] = "W", [64] = "E", [65] = "R", [67] = "T", [69] = "Y", [71] = "U",
        [72] = "1", [74] = "2", [76] = "3", [77] = "4", [79] = "5", [81] = "6", [83] = "7",
    };

    public static readonly IReadOnlyList<string> MicrophoneKeys =
        ["A", "S", "D", "F", "G", "Q", "W", "E", "R", "T", "1", "2", "3", "4", "5"];

    public static readonly IReadOnlyDictionary<int, string> DrumKeys = new Dictionary<int, string>
    {
        // General MIDI percussion is reduced to the ten pads available in the game.
        // Keep every standard drum voice audible by grouping related voices on its closest pad.
        [35] = "E", [36] = "E",
        [37] = "W", [38] = "W", [39] = "W", [40] = "W",
        [41] = "R", [43] = "R", [45] = "4", [47] = "4", [48] = "3", [50] = "3",
        [42] = "1", [44] = "1", [46] = "Q",
        [49] = "2", [52] = "2", [54] = "2", [55] = "2", [57] = "T", [58] = "T",
        [51] = "5", [53] = "5", [56] = "5", [59] = "5", [69] = "5", [70] = "5",
        [60] = "3", [61] = "3", [62] = "4", [63] = "4", [64] = "R",
        [65] = "T", [66] = "T", [67] = "T", [68] = "T", [71] = "T", [72] = "T",
        [73] = "T", [74] = "T", [75] = "T", [76] = "T", [77] = "T", [78] = "T",
        [79] = "T", [80] = "T", [81] = "T",
    };

    public const int MicrophoneMinMidi = 60;
    public const int MicrophoneMaxMidi = 74;

    public static int FoldToRange(int note, int low = 48, int high = 83)
    {
        var result = note;
        while (result < low)
        {
            result += 12;
        }

        while (result > high)
        {
            result -= 12;
        }

        return result;
    }

    public static int NearestWhite(int note, int low = 48, int high = 83, NearestWhiteDirection direction = NearestWhiteDirection.Down)
    {
        if (WhitePitchClasses.Contains(Mod12(note)))
        {
            return note;
        }

        var down = note - 1;
        var up = note + 1;
        var downValid = down >= low && WhitePitchClasses.Contains(Mod12(down));
        var upValid = up <= high && WhitePitchClasses.Contains(Mod12(up));

        return (downValid, upValid) switch
        {
            (true, true) => direction == NearestWhiteDirection.Up ? up : down,
            (true, false) => down,
            (false, true) => up,
            _ => Math.Clamp(note, low, high),
        };
    }

    public static double CalculateWhiteKeyRatio(IEnumerable<int> notes, int transpose = 0)
    {
        var values = notes as IReadOnlyCollection<int> ?? notes.ToArray();
        return values.Count == 0
            ? 0
            : values.Count(note => WhitePitchClasses.Contains(Mod12(note + transpose))) / (double)values.Count;
    }

    public static double CalculateRangeFitRatio(IEnumerable<int> notes, int low, int high, int transpose = 0)
    {
        var values = notes as IReadOnlyCollection<int> ?? notes.ToArray();
        return values.Count == 0
            ? 0
            : values.Count(note => note + transpose >= low && note + transpose <= high) / (double)values.Count;
    }

    public static int FindOptimalTranspose(IEnumerable<int> notes, MappingConfig config, int rangeLimit = 12)
    {
        var values = notes as IReadOnlyList<int> ?? notes.ToArray();
        if (values.Count == 0)
        {
            return 0;
        }

        Func<int, double> score = config.PreferNearestWhite
            ? transpose => CalculateWhiteKeyRatio(values, transpose)
            : transpose => CalculateRangeFitRatio(values, config.NoteRangeLow, config.NoteRangeHigh, transpose);

        var bestTranspose = 0;
        var bestScore = score(0);
        for (var transpose = -rangeLimit; transpose <= rangeLimit; transpose++)
        {
            var current = score(transpose);
            if (current > bestScore + 1e-9 ||
                (Math.Abs(current - bestScore) < 1e-9 && transpose < bestTranspose))
            {
                bestScore = current;
                bestTranspose = transpose;
            }
        }

        return bestTranspose;
    }

    public static string? MapPianoKey(int note, IReadOnlyDictionary<int, string>? customMap = null) =>
        (customMap ?? PianoKeys).GetValueOrDefault(note);

    public static string? MapMicrophoneKey(
        int note,
        int transpose,
        IReadOnlyDictionary<int, string>? customMap = null,
        int low = MicrophoneMinMidi,
        int high = MicrophoneMaxMidi)
    {
        var folded = FoldToRange(note + transpose, low, high);
        if (customMap is not null)
        {
            return customMap.GetValueOrDefault(folded);
        }

        if (high - low + 1 != MicrophoneKeys.Count)
        {
            return null;
        }

        var offset = folded - low;
        return offset >= 0 && offset < MicrophoneKeys.Count ? MicrophoneKeys[offset] : null;
    }

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
    public static int FindOptimalTranspose(IEnumerable<MidiNote> notes, MappingConfig config, int rangeLimit = 12)
    {
        var values = notes as IReadOnlyList<MidiNote> ?? notes.ToArray();
        if (values.Count == 0 || config.Instrument == InstrumentKind.Drums)
        {
            return 0;
        }

        if (config.CollisionStrategy == CollisionStrategy.OriginalFold)
        {
            return FindOptimalTranspose(values.Select(note => note.Note), config, rangeLimit);
        }

        var bestTranspose = 0;
        var bestLost = int.MaxValue;
        var bestRatio = double.MinValue;
        for (var transpose = -rangeLimit; transpose <= rangeLimit; transpose++)
        {
            var candidateConfig = config with { TransposeSemitones = transpose };
            var lost = config.CollisionStrategy == CollisionStrategy.SmartOctaveFold
                ? ScoreOctaveOffsets(values, candidateConfig, BuildOctaveOffsets(values, candidateConfig))
                : ScoreClusteredLoss(values, candidateConfig);
            var ratio = config.PreferNearestWhite
                ? CalculateWhiteKeyRatio(values.Select(note => note.Note), transpose)
                : CalculateRangeFitRatio(values.Select(note => note.Note), config.NoteRangeLow, config.NoteRangeHigh, transpose);
            var ratioTie = Math.Abs(ratio - bestRatio) < 1e-9;
            if (ratio > bestRatio + 1e-9 ||
                (ratioTie && lost < bestLost) ||
                (ratioTie && lost == bestLost && transpose < bestTranspose))
            {
                bestRatio = ratio;
                bestLost = lost;
                bestTranspose = transpose;
            }
        }

        return bestTranspose;
    }


    public static IReadOnlyDictionary<int, int> BuildOctaveOffsets(
        IEnumerable<MidiNote> notes,
        MappingConfig config,
        int maxOctaveJump = 2)
    {
        var values = notes as IReadOnlyList<MidiNote> ?? notes.ToArray();
        if (values.Count == 0 || config.Instrument == InstrumentKind.Drums)
        {
            return new Dictionary<int, int>();
        }

        var low = config.NoteRangeLow;
        var high = config.NoteRangeHigh;
        var transpose = config.TransposeSemitones;
        var distinct = values.Select(note => note.Note).Distinct().ToArray();
        var natural = distinct.ToDictionary(note => note, note => NaturalOctaveOffset(note, transpose, low, high));
        var offsets = new Dictionary<int, int>(natural);
        var bestScore = ScoreOctaveOffsets(values, config, offsets);
        var outOfRange = distinct
            .Where(note => note + transpose < low || note + transpose > high)
            .ToArray();
        RunOctaveSearch(values, config, offsets, ref bestScore, natural, outOfRange, low, high, maxOctaveJump);

        var stillColliding = FindCollidingPitches(values, config, offsets);
        var inRangeColliding = distinct
            .Where(note => natural[note] == 0 && offsets[note] == 0 && stillColliding.Contains(note))
            .ToArray();
        RunOctaveSearch(values, config, offsets, ref bestScore, natural, inRangeColliding, low, high, maxOctaveJump);

        return offsets
            .Where(pair => pair.Value != natural[pair.Key])
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static void RunOctaveSearch(
        IReadOnlyList<MidiNote> values,
        MappingConfig config,
        Dictionary<int, int> offsets,
        ref int bestScore,
        IReadOnlyDictionary<int, int> natural,
        IReadOnlyList<int> movable,
        int low,
        int high,
        int maxOctaveJump)
    {
        if (movable.Count == 0)
        {
            return;
        }

        var choices = new Dictionary<int, int[]>(movable.Count);
        foreach (var note in movable)
        {
            var options = new List<int>();
            for (var k = -4; k <= 4; k++)
            {
                var shifted = note + config.TransposeSemitones + 12 * k;
                if (shifted >= low && shifted <= high && Math.Abs(k - natural[note]) <= maxOctaveJump)
                {
                    options.Add(k);
                }
            }

            choices[note] = options.Distinct().ToArray();
        }

        for (var round = 0; round < 6; round++)
        {
            var improved = false;
            foreach (var note in movable)
            {
                foreach (var candidate in choices[note])
                {
                    if (candidate == offsets[note])
                    {
                        continue;
                    }

                    var previous = offsets[note];
                    offsets[note] = candidate;
                    var score = ScoreOctaveOffsets(values, config, offsets);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        improved = true;
                    }
                    else
                    {
                        offsets[note] = previous;
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }
    }

    private static HashSet<int> FindCollidingPitches(
        IReadOnlyList<MidiNote> notes,
        MappingConfig config,
        IReadOnlyDictionary<int, int> octaveOffsets)
    {
        var colliding = new HashSet<int>();
        var windowMs = Math.Max(0, config.ChordClusterWindowMs);
        var scaled = notes
            .Select(note => ScaleForScoring(note, config.Speed))
            .OrderBy(note => note.StartMs)
            .ThenBy(note => note.EndMs)
            .ThenByDescending(note => note.Note)
            .ToArray();

        var cluster = new List<MidiNote>();
        var clusterStart = 0;
        foreach (var note in scaled)
        {
            if (cluster.Count == 0)
            {
                cluster.Add(note);
                clusterStart = note.StartMs;
            }
            else if (note.StartMs - clusterStart <= windowMs)
            {
                cluster.Add(note);
            }
            else
            {
                CollectCollidingPitches(cluster, config, octaveOffsets, colliding);
                cluster = [note];
                clusterStart = note.StartMs;
            }
        }

        if (cluster.Count > 0)
        {
            CollectCollidingPitches(cluster, config, octaveOffsets, colliding);
        }

        return colliding;
    }

    private static void CollectCollidingPitches(
        IReadOnlyList<MidiNote> cluster,
        MappingConfig config,
        IReadOnlyDictionary<int, int> octaveOffsets,
        HashSet<int> colliding)
    {
        var byKey = new Dictionary<string, List<MidiNote>>(StringComparer.Ordinal);
        foreach (var note in cluster)
        {
            var key = MappedKeyForScoring(note, config, octaveOffsets);
            if (key is null)
            {
                continue;
            }

            if (!byKey.TryGetValue(key, out var list))
            {
                byKey[key] = list = [];
            }

            list.Add(note);
        }

        foreach (var group in byKey.Values.Where(list => list.Count > 1))
        {
            foreach (var note in group)
            {
                colliding.Add(note.Note);
            }
        }
    }

    private static int ScoreOctaveOffsets(
        IReadOnlyList<MidiNote> notes,
        MappingConfig config,
        IReadOnlyDictionary<int, int> octaveOffsets)
    {
        var windowMs = Math.Max(0, config.ChordClusterWindowMs);
        var scaled = notes
            .Select(note => ScaleForScoring(note, config.Speed))
            .OrderBy(note => note.StartMs)
            .ThenBy(note => note.EndMs)
            .ThenByDescending(note => note.Note)
            .ToArray();

        var lost = 0;
        var cluster = new List<MidiNote>();
        var clusterStart = 0;
        foreach (var note in scaled)
        {
            if (cluster.Count == 0)
            {
                cluster.Add(note);
                clusterStart = note.StartMs;
            }
            else if (note.StartMs - clusterStart <= windowMs)
            {
                cluster.Add(note);
            }
            else
            {
                lost += CountSwallowedNotes(cluster, config, octaveOffsets);
                cluster = [note];
                clusterStart = note.StartMs;
            }
        }

        if (cluster.Count > 0)
        {
            lost += CountSwallowedNotes(cluster, config, octaveOffsets);
        }

        return lost;
    }

    private static string? MappedKeyForScoring(
        MidiNote note,
        MappingConfig config,
        IReadOnlyDictionary<int, int> octaveOffsets)
    {
        var shifted = note.Note + config.TransposeSemitones + octaveOffsets.GetValueOrDefault(note.Note) * 12;
        if (config.Instrument == InstrumentKind.Microphone)
        {
            return MapMicrophoneKey(shifted, 0, config.CustomKeyMap, config.NoteRangeLow, config.NoteRangeHigh);
        }

        var folded = FoldToRange(shifted, config.NoteRangeLow, config.NoteRangeHigh);
        if (config.PreferNearestWhite)
        {
            folded = NearestWhite(folded, config.NoteRangeLow, config.NoteRangeHigh, config.NearestWhiteDirection);
        }

        return MapPianoKey(folded, config.CustomKeyMap);
    }

    private static int CountSwallowedNotes(
        IReadOnlyList<MidiNote> cluster,
        MappingConfig config,
        IReadOnlyDictionary<int, int> octaveOffsets)
    {
        var entries = new List<(MidiNote Note, string? Key)>();
        foreach (var note in cluster)
        {
            entries.Add((note, MappedKeyForScoring(note, config, octaveOffsets)));
        }

        var lost = 0;
        foreach (var group in entries.Where(entry => entry.Key is not null).GroupBy(entry => entry.Key!))
        {
            var ordered = group.OrderBy(entry => entry.Note.StartMs).ThenBy(entry => entry.Note.EndMs).ToArray();
            var pressEnd = ordered[0].Note.EndMs;
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].Note.StartMs <= pressEnd)
                {
                    lost++;
                }
                else
                {
                    pressEnd = ordered[i].Note.EndMs;
                }
            }
        }

        return lost;
    }

    public static int[] ResolveClusterOctaves(IReadOnlyList<MidiNote> ordered, MappingConfig config)
    {
        var offsets = new int[ordered.Count];
        if (ordered.Count == 0 || config.Instrument == InstrumentKind.Drums)
        {
            return offsets;
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var note = ordered[i];
            var naturalKey = ClusterKey(note, config, 0);
            if (naturalKey is null)
            {
                continue;
            }

            if (usedKeys.Add(naturalKey))
            {
                continue;
            }

            var natural = NaturalOctaveOffset(note.Note, config.TransposeSemitones, config.NoteRangeLow, config.NoteRangeHigh);
            var candidates = new List<int>();
            for (var k = -4; k <= 4; k++)
            {
                if (k == natural || Math.Abs(k - natural) > 2)
                {
                    continue;
                }

                var shifted = note.Note + config.TransposeSemitones + 12 * k;
                if (shifted < config.NoteRangeLow || shifted > config.NoteRangeHigh)
                {
                    continue;
                }

                candidates.Add(k);
            }

            candidates.Sort((left, right) =>
            {
                var deltaLeft = Math.Abs(left - natural);
                var deltaRight = Math.Abs(right - natural);
                return deltaLeft != deltaRight ? deltaLeft.CompareTo(deltaRight) : left.CompareTo(right);
            });

            foreach (var candidate in candidates)
            {
                var key = ClusterKey(note, config, candidate);
                if (key is not null && usedKeys.Add(key))
                {
                    offsets[i] = candidate;
                    break;
                }
            }
        }

        return offsets;
    }

    private static string? ClusterKey(MidiNote note, MappingConfig config, int octaveOffset)
    {
        var shifted = note.Note + config.TransposeSemitones + 12 * octaveOffset;
        if (config.Instrument == InstrumentKind.Microphone)
        {
            return MapMicrophoneKey(shifted, 0, config.CustomKeyMap, config.NoteRangeLow, config.NoteRangeHigh);
        }

        var folded = FoldToRange(shifted, config.NoteRangeLow, config.NoteRangeHigh);
        if (config.PreferNearestWhite)
        {
            folded = NearestWhite(folded, config.NoteRangeLow, config.NoteRangeHigh, config.NearestWhiteDirection);
        }

        return MapPianoKey(folded, config.CustomKeyMap);
    }

    private static int NaturalOctaveOffset(int note, int transpose, int low, int high)
    {
        var shifted = note + transpose;
        var offset = 0;
        while (shifted + 12 * offset < low)
        {
            offset++;
        }

        while (shifted + 12 * offset > high)
        {
            offset--;
        }

        return offset;
    }

    private static int ScoreClusteredLoss(IReadOnlyList<MidiNote> notes, MappingConfig config)
    {
        var windowMs = Math.Max(0, config.ChordClusterWindowMs);
        var scaled = notes
            .Select(note => ScaleForScoring(note, config.Speed))
            .OrderBy(note => note.StartMs)
            .ThenBy(note => note.EndMs)
            .ThenByDescending(note => note.Note)
            .ToArray();

        var lost = 0;
        foreach (var cluster in Cluster(scaled, windowMs))
        {
            var ordered = cluster
                .OrderByDescending(note => note.Note)
                .ThenByDescending(note => note.Velocity)
                .ThenBy(note => note.EndMs)
                .ToArray();
            var offsets = ResolveClusterOctaves(ordered, config);

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var unmapped = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                var key = ClusterKey(ordered[i], config, offsets[i]);
                if (key is null)
                {
                    unmapped++;
                }
                else
                {
                    keys.Add(key);
                }
            }

            lost += unmapped;
            lost += Math.Max(0, ordered.Length - unmapped - keys.Count);
            lost += Math.Max(0, keys.Count - Math.Max(1, config.MaxPolyphony));
        }

        return lost;
    }

    private static MidiNote ScaleForScoring(MidiNote note, double speed)
    {
        if (speed <= 0 || double.IsNaN(speed) || double.IsInfinity(speed))
        {
            return note;
        }

        var start = (int)(note.StartMs / speed);
        var end = Math.Max(start + PlaybackEventBuilder.MinimumKeyHoldMs, (int)(note.EndMs / speed));
        return note with { StartMs = start, EndMs = end };
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

}
