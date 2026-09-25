using MeowField.Infrastructure.Midi;

namespace MeowField.Infrastructure.Tests;

public sealed class MidiFileReaderTests
{
    [Fact]
    public async Task ReadAsync_ParsesStandardMidiWithMetricTiming()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meowfield-{Guid.NewGuid():N}.mid");
        await File.WriteAllBytesAsync(path,
        [
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
            0x00, 0x00, 0x00, 0x01, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x0D,
            0x00, 0x90, 0x3C, 0x64,
            0x83, 0x60, 0x80, 0x3C, 0x00,
            0x00, 0xFF, 0x2F, 0x00,
        ]);

        try
        {
            var result = await new DryWetMidiFileReader().ReadAsync(path);

            var note = Assert.Single(result.Notes);
            Assert.Equal(60, note.Note);
            Assert.Equal(100, note.Velocity);
            Assert.Equal(0, note.StartMs);
            Assert.Equal(500, note.EndMs);
            Assert.Equal(500, result.DurationMs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_UsesTempoChangesAndRunningStatusAcrossTracks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meowfield-{Guid.NewGuid():N}.mid");
        await File.WriteAllBytesAsync(path,
        [
            // Format 1, two tracks, 480 ticks per quarter note.
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
            0x00, 0x01, 0x00, 0x02, 0x01, 0xE0,

            // Tempo track: 120 BPM at tick 0, then 60 BPM at tick 480.
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x13,
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,
            0x83, 0x60, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40,
            0x00, 0xFF, 0x2F, 0x00,

            // Note track: the second note-on and note-off use running status.
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x14,
            0x00, 0x90, 0x3C, 0x64,
            0x83, 0x60, 0x3E, 0x64,
            0x00, 0x80, 0x3C, 0x00,
            0x83, 0x60, 0x3E, 0x00,
            0x00, 0xFF, 0x2F, 0x00,
        ]);

        try
        {
            var result = await new DryWetMidiFileReader().ReadAsync(path);

            Assert.Equal(2, result.Notes.Count);
            Assert.Equal((0, 500, 60, 100, 1),
                (result.Notes[0].StartMs, result.Notes[0].EndMs, result.Notes[0].Note, result.Notes[0].Velocity, result.Notes[0].Track));
            Assert.Equal((500, 1500, 62, 100, 1),
                (result.Notes[1].StartMs, result.Notes[1].EndMs, result.Notes[1].Note, result.Notes[1].Velocity, result.Notes[1].Track));
            Assert.Equal(1500, result.DurationMs);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
