namespace MeowField.Domain;

public static class ChordDetector
{
    public readonly record struct Detection(string Key, IReadOnlySet<int> PitchClasses);

    private static readonly (HashSet<int> Notes, string Key)[] Chords =
    [
        ([7, 11, 2, 5], "M"),
        ([0, 4, 7], "Z"),
        ([2, 5, 9], "X"),
        ([4, 7, 11], "C"),
        ([5, 9, 0], "V"),
        ([7, 11, 2], "B"),
        ([9, 0, 4], "N"),
    ];

    public static string? Detect(IEnumerable<int> notes)
    {
        return DetectWithMembers(notes)?.Key;
    }

    public static Detection? DetectWithMembers(IEnumerable<int> notes)
    {
        var pitchClasses = notes.Select(note => ((note % 12) + 12) % 12).ToHashSet();
        if (pitchClasses.Count == 0)
        {
            return null;
        }

        var chord = Chords.FirstOrDefault(candidate => candidate.Notes.IsSubsetOf(pitchClasses));
        return chord.Key is null ? null : new Detection(chord.Key, chord.Notes);
    }
}
