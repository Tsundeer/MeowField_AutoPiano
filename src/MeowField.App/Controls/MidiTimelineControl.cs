using System.Windows;
using System.Windows.Media;
using MeowField.Domain;

namespace MeowField.App.Controls;

public sealed class MidiTimelineControl : FrameworkElement
{
    public static readonly DependencyProperty NotesProperty = DependencyProperty.Register(
        nameof(Notes), typeof(IReadOnlyList<MidiNote>), typeof(MidiTimelineControl),
        new FrameworkPropertyMetadata(Array.Empty<MidiNote>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationMsProperty = DependencyProperty.Register(
        nameof(DurationMs), typeof(int), typeof(MidiTimelineControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CursorMsProperty = DependencyProperty.Register(
        nameof(CursorMs), typeof(int), typeof(MidiTimelineControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MidiNote> Notes
    {
        get => (IReadOnlyList<MidiNote>)GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public int DurationMs { get => (int)GetValue(DurationMsProperty); set => SetValue(DurationMsProperty, value); }
    public int CursorMs { get => (int)GetValue(CursorMsProperty); set => SetValue(CursorMsProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2) return;

        var surface = TryFindResource("Brush.Surface.Subtle") as Brush ?? Brushes.WhiteSmoke;
        var border = TryFindResource("Brush.Border.Default") as Brush ?? Brushes.LightGray;
        var accent = TryFindResource("Brush.Accent.Primary") as Brush ?? Brushes.HotPink;
        drawingContext.DrawRectangle(surface, new Pen(border, 1), new Rect(0, 0, width, height));

        var duration = Math.Max(1, DurationMs);
        for (var fraction = 0.25; fraction < 1; fraction += 0.25)
        {
            var x = width * fraction;
            drawingContext.DrawLine(new Pen(border, 0.5), new Point(x, 0), new Point(x, height));
        }

        var notes = Notes ?? Array.Empty<MidiNote>();
        if (notes.Count > 0)
        {
            var min = notes.Min(note => note.Note);
            var max = Math.Max(min + 1, notes.Max(note => note.Note));
            var pitchRange = Math.Max(1, max - min + 1);
            var step = Math.Max(1, notes.Count / 1400);
            for (var index = 0; index < notes.Count; index += step)
            {
                var note = notes[index];
                var x = Math.Clamp(note.StartMs / (double)duration * width, 0, width - 1);
                var noteWidth = Math.Max(1.5, (note.EndMs - note.StartMs) / (double)duration * width);
                var y = (max - note.Note) / (double)pitchRange * Math.Max(1, height - 8) + 4;
                var noteHeight = Math.Max(2, Math.Min(7, height / Math.Max(12d, pitchRange / 2d)));
                var opacity = 0.35 + Math.Clamp(note.Velocity / 127d, 0, 1) * 0.55;
                var accentColor = accent is SolidColorBrush solid ? solid.Color : Colors.HotPink;
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(accentColor) { Opacity = opacity }, null,
                    new Rect(x, y, Math.Min(noteWidth, width - x), noteHeight), 2, 2);
            }
        }

        var cursorX = Math.Clamp(CursorMs / (double)duration * width, 0, width);
        drawingContext.DrawLine(new Pen(accent, 1.5), new Point(cursorX, 0), new Point(cursorX, height));
    }
}
