namespace MeowField.Domain;

public static class ChordDetector
{
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
        var pitchClasses = notes.Select(note => ((note % 12) + 12) % 12).ToHashSet();
        if (pitchClasses.Count == 0)
        {
            return null;
        }

        return Chords.FirstOrDefault(chord => chord.Notes.IsSubsetOf(pitchClasses)).Key;
    }
}
