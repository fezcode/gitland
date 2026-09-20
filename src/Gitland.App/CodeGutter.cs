using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Gitland.App;

/// <summary>Line numbers aligned to the native editor's shaped lines and scroll offset.</summary>
public sealed class CodeGutter : Control {
    readonly MergeEditor _editor;
    public CodeGutter(MergeEditor editor) {
        _editor = editor; ClipToBounds = true; IsHitTestVisible = false;
        editor.LayoutUpdated += (_, _) => InvalidateVisual();
        editor.ViewChanged += InvalidateVisual;
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => InvalidateVisual());
    }
    public override void Render(DrawingContext context) {
        context.FillRectangle(Palette.Bar, new Rect(Bounds.Size));
        context.FillRectangle(Palette.Hairline, new Rect(Math.Max(0, Bounds.Width - 1), 0, 1, Bounds.Height));
        var presenter = _editor.Presenter;
        if (presenter?.TranslatePoint(default, this) is not { } origin) return;
        string source = _editor.Text ?? "";
        double y = origin.Y; int number = _editor.LineNumberStart, highlight = 0, previousOffset = 0, previousNumber = -1;
        foreach (var line in presenter.TextLayout.TextLines) {
            if (y > Bounds.Height) break;
            int offset = Math.Clamp(line.FirstTextSourceIndex, previousOffset, source.Length);
            number += source.AsSpan(previousOffset, offset - previousOffset).Count('\n'); previousOffset = offset;
            bool continuation = number == previousNumber; previousNumber = number;
            while (highlight < _editor.Highlights.Count && _editor.Highlights[highlight].Start + _editor.Highlights[highlight].Length <= line.FirstTextSourceIndex) highlight++;
            bool marker = highlight < _editor.Highlights.Count && _editor.Highlights[highlight].Start < line.FirstTextSourceIndex + line.Length && _editor.Highlights[highlight].Kind == Gitland.Core.MergeLineKind.Marker;
            if (y + line.Height >= 0) {
                bool active = _editor.ActiveStart >= 0 && line.FirstTextSourceIndex < _editor.ActiveStart + _editor.ActiveLength && line.FirstTextSourceIndex + line.Length > _editor.ActiveStart;
                if (active) { context.FillRectangle(Palette.SelectedSurface, new Rect(0, y, Bounds.Width - 1, line.Height)); context.FillRectangle(Palette.Accent, new Rect(0, y, 2, line.Height)); }
                var text = new FormattedText(continuation ? "·" : marker ? "!" : number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Palette.Mono), 10, marker ? Palette.Amber : active ? Palette.Ink : Palette.Faint);
                context.DrawText(text, new Point(Bounds.Width - text.Width - 10, y + (line.Height - text.Height) / 2));
            }
            y += line.Height;
        }
    }
}
