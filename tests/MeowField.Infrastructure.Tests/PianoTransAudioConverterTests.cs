using MeowField.Infrastructure.Audio;

namespace MeowField.Infrastructure.Tests;

public sealed class PianoTransAudioConverterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meowfield-pianotrans-tests-{Guid.NewGuid():N}");

    public PianoTransAudioConverterTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void FindGeneratedMidi_ReturnsInputNamePlusMid_WhenPianoTransAppendsExtension()
    {
        var stem = $"meowfield-{Guid.NewGuid():N}";
        var actual = Path.Combine(_directory, $"{stem}.flac.mid");
        File.WriteAllText(actual, "midi");

        var found = PianoTransAudioConverter.FindGeneratedMidi(
            _directory, stem,
            Path.Combine(_directory, $"{stem}.mid"),
            Path.Combine(_directory, $"{stem}.flac"),
            Path.Combine(_directory, "output.mid"));

        Assert.Equal(actual, found);
    }

    [Fact]
    public void FindGeneratedMidi_ReturnsExpectedStemMid_WhenPresent()
    {
        var stem = $"meowfield-{Guid.NewGuid():N}";
        var expected = Path.Combine(_directory, $"{stem}.mid");
        File.WriteAllText(expected, "midi");

        var found = PianoTransAudioConverter.FindGeneratedMidi(
            _directory, stem, expected,
            Path.Combine(_directory, $"{stem}.wav"),
            Path.Combine(_directory, "output.mid"));

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindGeneratedMidi_FallsBackToGenericOutput()
    {
        var stem = $"meowfield-{Guid.NewGuid():N}";
        var generic = Path.Combine(_directory, "output.mid");
        File.WriteAllText(generic, "midi");

        var found = PianoTransAudioConverter.FindGeneratedMidi(
            _directory, stem,
            Path.Combine(_directory, $"{stem}.mid"),
            Path.Combine(_directory, $"{stem}.flac"),
            generic);

        Assert.Equal(generic, found);
    }

    [Fact]
    public void FindGeneratedMidi_FindsNestedStemOutput()
    {
        var stem = $"meowfield-{Guid.NewGuid():N}";
        var nestedDirectory = Path.Combine(_directory, "output");
        Directory.CreateDirectory(nestedDirectory);
        var nested = Path.Combine(nestedDirectory, $"{stem}.flac.mid");
        File.WriteAllText(nested, "midi");

        var found = PianoTransAudioConverter.FindGeneratedMidi(
            _directory, stem,
            Path.Combine(_directory, $"{stem}.mid"),
            Path.Combine(_directory, $"{stem}.flac"),
            Path.Combine(_directory, "output.mid"));

        Assert.Equal(nested, found);
    }

    [Fact]
    public void FindGeneratedMidi_ReturnsNull_WhenNoOutputExists()
    {
        var stem = $"meowfield-{Guid.NewGuid():N}";

        var found = PianoTransAudioConverter.FindGeneratedMidi(
            _directory, stem,
            Path.Combine(_directory, $"{stem}.mid"),
            Path.Combine(_directory, $"{stem}.flac"),
            Path.Combine(_directory, "output.mid"));

        Assert.Null(found);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
