using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MeowField.Application;
using MeowField.Domain;

namespace MeowField.Infrastructure.Midi;

public sealed class DryWetMidiFileReader : IMidiFileReader
{
    public async Task<ParsedMidi> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return await Task.Run(() => Read(fullPath, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static ParsedMidi Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var midiFile = MidiFile.Read(path);
        if (midiFile.OriginalFormat == MidiFileFormat.MultiSequence)
        {
            throw new InvalidDataException("MIDI format 2 contains independent sequences and cannot be loaded as one playback timeline.");
        }

        var tempoMap = midiFile.GetTempoMap();
        var notes = new List<MidiNote>();
        var tracks = midiFile.GetTrackChunks().ToArray();
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            foreach (var note in tracks[trackIndex].GetNotes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                notes.Add(new MidiNote(
                    ToMilliseconds(note.TimeAs<MetricTimeSpan>(tempoMap)),
                    ToMilliseconds(note.EndTimeAs<MetricTimeSpan>(tempoMap)),
                    note.NoteNumber,
                    note.Velocity,
                    note.Channel,
                    trackIndex));
            }
        }

        notes.Sort(static (left, right) =>
        {
            var result = left.StartMs.CompareTo(right.StartMs);
            if (result != 0) return result;
            result = left.Track.CompareTo(right.Track);
            if (result != 0) return result;
            result = left.Note.CompareTo(right.Note);
            return result != 0 ? result : left.EndMs.CompareTo(right.EndMs);
        });

        cancellationToken.ThrowIfCancellationRequested();
        var durationMs = ToMilliseconds(midiFile.GetDuration<MetricTimeSpan>());
        if (notes.Count > 0)
        {
            durationMs = Math.Max(durationMs, notes.Max(note => note.EndMs));
        }

        return new ParsedMidi(notes, durationMs, path);
    }

    private static int ToMilliseconds(MetricTimeSpan value)
    {
        var milliseconds = value.TotalMicroseconds / 1000d;
        return milliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero));
    }
}
