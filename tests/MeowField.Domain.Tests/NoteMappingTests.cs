using MeowField.Domain;

namespace MeowField.Domain.Tests;

public sealed class NoteMappingTests
{
    [Theory]
    [InlineData(47, 59)]
    [InlineData(48, 48)]
    [InlineData(84, 72)]
    [InlineData(96, 72)]
    public void FoldToRange_MatchesLegacyOctaveFolding(int note, int expected)
    {
        Assert.Equal(expected, NoteMapping.FoldToRange(note, 48, 83));
    }

    [Theory]
    [InlineData(61, 60)]
    [InlineData(63, 62)]
    [InlineData(66, 65)]
    public void NearestWhite_PrefersLowerNoteOnTie(int note, int expected)
    {
        Assert.Equal(expected, NoteMapping.NearestWhite(note));
    }

    [Fact]
    public void FindOptimalTranspose_UsesFirstStrictlyBetterCandidate()
    {
        var notes = new[] { 61, 63, 66 };
        var result = NoteMapping.FindOptimalTranspose(notes, new MappingConfig());
        Assert.Equal(-11, result);
        Assert.Equal(1, NoteMapping.CalculateWhiteKeyRatio(notes, result));
    }

    [Fact]
    public void CollisionStrategy_DefaultsToPerNoteMinimal()
    {
        var config = new MappingConfig();
        Assert.Equal(CollisionStrategy.PerNoteMinimal, config.CollisionStrategy);
    }

    [Fact]
    public void BuildOctaveOffsets_MovesOutOfRangeNoteToAvoidSameKeyCollision()
    {
        MidiNote[] notes =
        [
            new(0, 500, 43, 100, 0, 0),
            new(0, 500, 55, 100, 0, 0),
        ];

        var offsets = NoteMapping.BuildOctaveOffsets(notes, new MappingConfig { ChordMode = ChordMode.Off });

        // G2 and G3 would both fold to key G; the smart fold moves G2 up another octave instead.
        Assert.Equal(2, offsets.GetValueOrDefault(43));
        Assert.False(offsets.ContainsKey(55));
    }

    [Fact]
    public void FindOptimalTranspose_OriginalFold_UsesLegacyRatioHeuristic()
    {
        MidiNote[] notes =
        [
            new(0, 100, 61, 100, 0, 0),
            new(0, 100, 63, 100, 0, 0),
            new(0, 100, 66, 100, 0, 0),
        ];

        var result = NoteMapping.FindOptimalTranspose(notes, new MappingConfig
        {
            CollisionStrategy = CollisionStrategy.OriginalFold,
        });

        Assert.Equal(-11, result);
        Assert.Equal(1, NoteMapping.CalculateWhiteKeyRatio(notes.Select(note => note.Note), result));
    }

    [Fact]
    public void ResolveClusterOctaves_MovesCollidingNoteToNearestFreeOctave()
    {
        MidiNote[] cluster =
        [
            new(0, 500, 55, 100, 0, 0),
            new(0, 500, 43, 100, 0, 0),
        ];

        var offsets = NoteMapping.ResolveClusterOctaves(cluster, new MappingConfig { ChordMode = ChordMode.Off });

        // G3 keeps its natural key; G2 is moved one octave up (G4) to avoid the same-key collision.
        Assert.Equal([0, 2], offsets);
    }

    [Fact]
    public void ResolveClusterOctaves_KeepsNonCollidingNotesAtNaturalOctave()
    {
        MidiNote[] cluster =
        [
            new(0, 500, 43, 100, 0, 0),
        ];

        var offsets = NoteMapping.ResolveClusterOctaves(cluster, new MappingConfig { ChordMode = ChordMode.Off });

        Assert.Equal([0], offsets);
    }

    [Fact]
    public void FindOptimalTranspose_WithMidiNotes_PrioritizesWhiteKeyRatioAndLowerTranspose()
    {
        MidiNote[] notes =
        [
            new(0, 100, 43, 90, 0, 0),
            new(0, 100, 55, 80, 0, 0),
            new(0, 100, 61, 70, 0, 0),
            new(0, 100, 63, 60, 0, 0),
        ];
        var config = new MappingConfig { ChordMode = ChordMode.Off, CollisionStrategy = CollisionStrategy.PerNoteMinimal };

        var result = NoteMapping.FindOptimalTranspose(notes, config);
        var candidates = Enumerable.Range(-12, 25)
            .Select(transpose => new
            {
                Transpose = transpose,
                Ratio = NoteMapping.CalculateWhiteKeyRatio(notes.Select(note => note.Note), transpose),
            })
            .ToArray();
        var bestRatio = candidates.Max(candidate => candidate.Ratio);
        var expected = candidates
            .Where(candidate => Math.Abs(candidate.Ratio - bestRatio) < 1e-9)
            .Min(candidate => candidate.Transpose);

        Assert.Equal(expected, result);
        Assert.Equal(bestRatio, NoteMapping.CalculateWhiteKeyRatio(notes.Select(note => note.Note), result));
    }

    [Theory]
    [InlineData(60, "A")]
    [InlineData(74, "5")]
    [InlineData(48, "A")]
    [InlineData(86, "5")]
    public void MicrophoneMapping_MatchesLegacyFifteenKeyLayout(int note, string expected)
    {
        Assert.Equal(expected, NoteMapping.MapMicrophoneKey(note, 0));
    }
}
