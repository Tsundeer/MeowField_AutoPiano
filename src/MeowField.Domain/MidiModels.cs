namespace MeowField.Domain;

public sealed record MidiNote(
    int StartMs,
    int EndMs,
    int Note,
    int Velocity,
    int Channel,
    int Track);

public sealed record ParsedMidi(
    IReadOnlyList<MidiNote> Notes,
    int DurationMs,
    string? SourcePath = null);

public enum PlayEventType
{
    Down,
    Up,
}

public sealed record PlayEvent(
    int TimeMs,
    PlayEventType Type,
    string Key,
    string Source,
    int? Note = null);

public enum InstrumentKind
{
    Piano,
    Drums,
    Microphone,
}

public enum InputMode
{
    SendInput,
    WindowMessage,
}

public enum ChordMode
{
    Off,
    Prefer,
    Melody,
    Smart,
}

public enum CollisionStrategy
{
    OriginalFold,
    SmartOctaveFold,
    PerNoteMinimal,
}

public sealed record HotkeyConfig(
    string PlayPause = "ctrl+shift+c",
    string Stop = "f9");

public sealed record MappingConfig
{
    public InstrumentKind Instrument { get; init; } = InstrumentKind.Piano;
    public InputMode InputMode { get; init; } = InputMode.SendInput;
    public int? MidiChannelFilter { get; init; }
    public int NoteRangeLow { get; init; } = 48;
    public int NoteRangeHigh { get; init; } = 83;
    public bool PreferNearestWhite { get; init; } = true;
    public int TransposeSemitones { get; init; }
    public double Speed { get; init; } = 1.0;
    public int MaxPolyphony { get; init; } = 10;
    public ChordMode ChordMode { get; init; } = ChordMode.Prefer;
    public CollisionStrategy CollisionStrategy { get; init; } = CollisionStrategy.PerNoteMinimal;
    public bool KeepMelodyTopNote { get; init; } = true;
    public int ChordClusterWindowMs { get; init; } = 40;
    public bool AutoTranspose { get; init; }
    public int LinkLatencyMs { get; init; }
    public HotkeyConfig Hotkeys { get; init; } = new();
    public IReadOnlyDictionary<int, string>? CustomKeyMap { get; init; }

    public bool ChordPrefer => ChordMode != ChordMode.Off;
    public bool IsMicrophoneMode => Instrument == InstrumentKind.Microphone;

    public void Validate()
    {
        if (Speed <= 0 || double.IsNaN(Speed) || double.IsInfinity(Speed))
        {
            throw new ArgumentOutOfRangeException(nameof(Speed), "Speed must be a finite value greater than zero.");
        }

        if (NoteRangeLow is < 0 or > 127 || NoteRangeHigh is < 0 or > 127 || NoteRangeLow > NoteRangeHigh)
        {
            throw new ArgumentOutOfRangeException(nameof(NoteRangeLow), "MIDI note range must be within 0..127 and low must not exceed high.");
        }

        if (MaxPolyphony < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPolyphony));
        }

        if (ChordClusterWindowMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ChordClusterWindowMs));
        }
    }
}
