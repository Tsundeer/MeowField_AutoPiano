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

    public static int NearestWhite(int note, int low = 48, int high = 83)
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
            (true, true) => down,
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
            if (current > bestScore)
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
}
