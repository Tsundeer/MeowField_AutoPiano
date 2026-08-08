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
