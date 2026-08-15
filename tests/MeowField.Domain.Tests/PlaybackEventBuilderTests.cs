using MeowField.Domain;

namespace MeowField.Domain.Tests;

public sealed class PlaybackEventBuilderTests
{
    [Fact]
    public void PianoChord_EmitsChordAndTopMelodyLikeLegacyBuilder()
    {
        MidiNote[] notes =
        [
            new(100, 400, 60, 80, 0, 0),
            new(100, 350, 64, 90, 0, 0),
            new(100, 300, 67, 100, 0, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig { ChordMode = ChordMode.Prefer });

        Assert.Equal(4, events.Count(item => item.Type == PlayEventType.Down));
        Assert.Contains(events, item => item.Type == PlayEventType.Down && item.Key == "Z" && item.Source == "chord");
        Assert.Contains(events, item => item.Type == PlayEventType.Down && item.Key == "Q");
        Assert.Contains(events, item => item.Type == PlayEventType.Down && item.Key == "E");
        Assert.Contains(events, item => item.Type == PlayEventType.Down && item.Key == "T");
    }

    [Theory]
    [InlineData(ChordMode.Melody)]
    [InlineData(ChordMode.Smart)]
    public void NonCompressingChordModes_PreserveEveryMappedNote(ChordMode chordMode)
    {
        MidiNote[] notes =
        [
            new(100, 400, 60, 80, 0, 0),
            new(100, 350, 64, 90, 0, 0),
            new(100, 300, 67, 100, 0, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig
        {
            ChordMode = chordMode,
            MaxPolyphony = 21,
        });

        Assert.Equal(notes.Length + 1, events.Count(item => item.Type == PlayEventType.Down));
    }

    [Fact]
    public void Polyphony_PicksHighestVelocityAfterGroupingByKey()
    {
        MidiNote[] notes =
        [
            new(0, 100, 60, 30, 0, 0),
            new(0, 100, 62, 90, 0, 0),
            new(0, 100, 64, 60, 0, 0),
        ];
        var config = new MappingConfig
        {
            ChordMode = ChordMode.Off,
            MaxPolyphony = 2,
        };

        var downs = PlaybackEventBuilder.Build(notes, config).Where(item => item.Type == PlayEventType.Down).ToArray();

        Assert.Equal(["E", "W"], downs.Select(item => item.Key).Order().ToArray());
    }

    [Fact]
    public void CollisionStrategy_OriginalFold_CollapsesOctaveDoubledNotes()
    {
        MidiNote[] notes =
        [
            new(0, 500, 43, 100, 0, 0),
            new(0, 500, 55, 100, 0, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig
        {
            ChordMode = ChordMode.Off,
            CollisionStrategy = CollisionStrategy.OriginalFold,
        });

        // Legacy behavior: G2 folds onto G3 and both notes share the same key.
        Assert.Equal(["G"], events.Where(item => item.Type == PlayEventType.Down).Select(item => item.Key).ToArray());
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void CollisionStrategy_SmartOctaveFold_KeepsOctaveDoubledNotesDistinct()
    {
        MidiNote[] notes =
        [
            new(0, 500, 43, 100, 0, 0),
            new(0, 500, 55, 100, 0, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig
        {
            ChordMode = ChordMode.Off,
            CollisionStrategy = CollisionStrategy.SmartOctaveFold,
        });

        // G2 is folded to G4 instead of colliding with G3, so both notes keep their own press.
        Assert.Equal(["G", "T"], events.Where(item => item.Type == PlayEventType.Down).Select(item => item.Key).Order().ToArray());
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void CollisionStrategy_PerNoteMinimal_KeepsOctaveDoubledNotesDistinct()
    {
        MidiNote[] notes =
        [
            new(0, 500, 43, 100, 0, 0),
            new(0, 500, 55, 100, 0, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig
        {
            ChordMode = ChordMode.Off,
            CollisionStrategy = CollisionStrategy.PerNoteMinimal,
        });

        Assert.Equal(["G", "T"], events.Where(item => item.Type == PlayEventType.Down).Select(item => item.Key).Order().ToArray());
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void Drums_FilterChannelAndKeepRawNoteMapping()
    {
        MidiNote[] notes =
        [
            new(0, 100, 48, 100, 9, 0),
            new(0, 100, 50, 100, 1, 0),
        ];
        var config = new MappingConfig
        {
            Instrument = InstrumentKind.Drums,
            MidiChannelFilter = 9,
        };

        var events = PlaybackEventBuilder.Build(notes, config);

        Assert.Equal(2, events.Count);
        Assert.All(events, item => Assert.Equal("3", item.Key));
        Assert.All(events, item => Assert.Equal("drum", item.Source));
    }

    [Fact]
    public void Drums_GroupsStandardPercussionWithoutDroppingSupportedVoices()
    {
        MidiNote[] notes =
        [
            new(0, 100, 41, 100, 9, 0), new(100, 200, 45, 100, 9, 0),
            new(200, 300, 54, 100, 9, 0), new(300, 400, 56, 100, 9, 0),
            new(400, 500, 69, 100, 9, 0),
        ];

        var events = PlaybackEventBuilder.Build(notes, new MappingConfig { Instrument = InstrumentKind.Drums, MidiChannelFilter = 9 });

        Assert.Equal(10, events.Count);
        Assert.Equal(["R", "4", "2", "5", "5"], events.Where(item => item.Type == PlayEventType.Down).Select(item => item.Key).ToArray());
    }

    [Fact]
    public void CustomMap_IsUsedForDrumsWithoutFallingBackToPianoKeys()
    {
        MidiNote[] notes = [new(0, 100, 48, 100, 9, 0)];
        var config = new MappingConfig { Instrument = InstrumentKind.Drums, MidiChannelFilter = 9, CustomKeyMap = new Dictionary<int, string> { [48] = "K" } };

        var events = PlaybackEventBuilder.Build(notes, config);

        Assert.Equal(["K", "K"], events.Select(item => item.Key).ToArray());
    }

    [Fact]
    public void Speed_ScalesStartAndGuaranteesMinimumPhysicalHold()
    {
        MidiNote[] notes = [new(9, 10, 60, 100, 0, 0)];
        var config = new MappingConfig { Speed = 2, ChordMode = ChordMode.Off };

        var events = PlaybackEventBuilder.Build(notes, config);

        Assert.Equal(4, events[0].TimeMs);
        Assert.Equal(49, events[1].TimeMs);
    }
}
