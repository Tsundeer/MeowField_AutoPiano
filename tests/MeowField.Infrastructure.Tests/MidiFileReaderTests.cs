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
}
