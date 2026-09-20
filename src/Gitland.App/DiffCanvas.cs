using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Gitland.Core;

namespace Gitland.App;

public sealed class DiffCanvas : Control {
    public const double LineHeight = 26;
    public IReadOnlyList<DiffRow> Rows { get; private set; } = [];
    public bool Unified { get; private set; }
    public double ScrollTop { get; set; }
    public double ViewHeight { get; set; } = 800;
    public double HorizontalOffset { get; set; }
    public string Search { get; set; } = "";
    public int ActiveRow { get; set; } = -1;
    public double CodeSize { get; set; } = 14;
    readonly Dictionary<string, FormattedText> _cache = new();
    int _selectStart = -1, _selectEnd = -1; bool _rightSide;
    public event Action? ExpandRequested;
    public DiffCanvas() { Focusable = true; ClipToBounds = true; }
    public void RefreshAppearance() { _cache.Clear(); InvalidateVisual(); }
    public bool AlignedColumn { get; private set; }
    public void SetAlignedRows(IReadOnlyList<DiffRow> rows) {
        SetRows(rows, false); Unified = true; AlignedColumn = true;
    }
    public void SetRows(IReadOnlyList<DiffRow> rows, bool unified) {
        AlignedColumn = false;
        Unified = unified;
        Rows = unified ? rows.SelectMany(r => r.Kind == ChangeKind.Modified ? new[] { r with { Right = null, RightNumber = null, Kind = ChangeKind.Removed }, r with { Left = null, LeftNumber = null, Kind = ChangeKind.Added } } : new[] { r }).ToArray() : rows;
        Height = Math.Max(1, Rows.Count) * LineHeight + 50;
        _cache.Clear(); _selectStart = _selectEnd = -1; InvalidateVisual();
    }
    FormattedText Format(string text, IBrush color, double? size = null) {
        string key = $"{color}|{size ?? CodeSize}|{text}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (_cache.Count > 6000) _cache.Clear();
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Palette.Mono), size ?? CodeSize, color);
        formatted.SetFontFeatures(MergeEditor.LiteralCodeFeatures);
        return _cache[key] = formatted;
    }
    double CharWidth => Format("M", Palette.Ink).Width;
    public override void Render(DrawingContext ctx) {
        ctx.FillRectangle(Palette.Ground, new Rect(Bounds.Size));
        double half = (Bounds.Width - 22) / 2;
        int start = Math.Max(0, (int)(ScrollTop / LineHeight) - 1), end = Math.Min(Rows.Count, (int)((ScrollTop + ViewHeight) / LineHeight) + 2);
        for (int i = start; i < end; i++) {
            var row = Rows[i]; double y = i * LineHeight;
            if (row.Kind == ChangeKind.Fold) {
                ctx.FillRectangle(Palette.Bar, new Rect(0, y, Bounds.Width, LineHeight));
                ctx.DrawText(Format($"···  {row.Hidden} unchanged lines · click to expand", Palette.Faint, 12), new Point(64, y + 4)); continue;
            }
            if (Unified) {
                var text = row.Kind == ChangeKind.Removed ? row.Left : row.Right ?? row.Left;
                DrawSide(ctx, i, text, row.RightNumber ?? row.LeftNumber, row.Kind, 0, Bounds.Width, y, AlignedColumn ? row.Kind == ChangeKind.Removed ? row.Right : row.Left : null);
            } else {
                DrawSide(ctx, i, row.Left, row.LeftNumber, row.Kind is ChangeKind.Modified or ChangeKind.Removed ? ChangeKind.Removed : ChangeKind.Equal, 0, half, y, row.Kind == ChangeKind.Modified ? row.Right : null);
                DrawSide(ctx, i, row.Right, row.RightNumber, row.Kind is ChangeKind.Modified or ChangeKind.Added ? ChangeKind.Added : ChangeKind.Equal, half + 22, half, y, row.Kind == ChangeKind.Modified ? row.Left : null);
                ctx.FillRectangle(Palette.Bar, new Rect(half, y, 22, LineHeight));
                if (row.Kind != ChangeKind.Equal) {
                    ctx.FillRectangle(row.Kind == ChangeKind.Added ? Palette.AddedWord : row.Kind == ChangeKind.Removed ? Palette.RemovedWord : Palette.Selection, new Rect(half + 5, y + 2, 12, LineHeight - 4), 3);
                }
            }
            if (i == ActiveRow) {
                ctx.FillRectangle(Palette.Accent, new Rect(0, y, 3, LineHeight));
                ctx.DrawRectangle(null, new Pen(Palette.Hairline, 1), new Rect(3.5, y + 0.5, Bounds.Width - 4, LineHeight - 1));
            }
        }
    }
    void DrawSide(DrawingContext ctx, int row, string? text, int? line, ChangeKind kind, double x, double width, double y, string? other) {
        if (width < 1) return;
        using var clip = ctx.PushClip(new Rect(x, y, width, LineHeight));
        var bg = kind == ChangeKind.Added ? Palette.AddedFill : kind == ChangeKind.Removed ? Palette.RemovedFill : Palette.Ground;
        ctx.FillRectangle(bg, new Rect(x, y, width, LineHeight));
        if (_selectStart >= 0 && row >= Math.Min(_selectStart, _selectEnd) && row <= Math.Max(_selectStart, _selectEnd) && (Unified || (x > 0) == _rightSide)) ctx.FillRectangle(Palette.Selection, new Rect(x, y, width, LineHeight));
        if (text == null) { ctx.FillRectangle(Palette.Bar, new Rect(x, y, width, LineHeight)); return; }
        ctx.DrawText(Format(line?.ToString() ?? "", Palette.Faint, 11), new Point(x + 12, y + 5));
        ctx.DrawText(Format(kind == ChangeKind.Added ? "+" : kind == ChangeKind.Removed ? "−" : "", kind == ChangeKind.Added ? Palette.Green : Palette.Red), new Point(x + 42, y + 3));
        using var codeClip = ctx.PushClip(new Rect(x + 59, y, Math.Max(0, width - 59), LineHeight));
        double codeX = x + 64 - HorizontalOffset;
        text = text.Replace("\t", "    "); other = other?.Replace("\t", "    ");
        if (other != null) {
            var (spanStart, length) = DiffEngine.ChangedSpan(text, other);
            if (length > 0) ctx.FillRectangle(kind == ChangeKind.Added ? Palette.AddedWord : Palette.RemovedWord, new Rect(codeX + spanStart * CharWidth, y + 2, length * CharWidth, LineHeight - 4), 2);
        }
        if (Search.Length > 0) for (int at = 0; at < text.Length;) {
            int index = text.IndexOf(Search, at, StringComparison.OrdinalIgnoreCase); if (index < 0) break;
            ctx.FillRectangle(Palette.SearchFill, new Rect(codeX + index * CharWidth, y + 2, Search.Length * CharWidth, LineHeight - 4), 2); at = index + Search.Length;
        }
        // Shape only the horizontally visible part of long lines (for example minified JSON).
        int firstChar = Math.Max(0, (int)(HorizontalOffset / CharWidth) - 1);
        int visibleChars = Math.Max(1, (int)(width / CharWidth) + 3);
        if (firstChar >= text.Length) return;
        text = text.Substring(firstChar, Math.Min(visibleChars, text.Length - firstChar));
        codeX += firstChar * CharWidth;
        // Tokenize only visible source; unrecognized languages remain readable plain text.
        int pos = 0;
        foreach (Match token in Regex.Matches(text, "(//.*$|#.*$|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|\\b(?:const|let|var|function|return|if|else|async|await|export|import|from|class|public|private|new|true|false|null|using|namespace|void|static|throw|try|catch|interface|type|readonly)\\b|\\b\\d+(?:\\.\\d+)?\\b)", RegexOptions.None, TimeSpan.FromMilliseconds(100))) {
            if (token.Index > pos) ctx.DrawText(Format(text[pos..token.Index], Palette.Ink), new Point(codeX + pos * CharWidth, y + 3));
            IBrush color = token.Value.StartsWith("//") || token.Value.StartsWith('#') ? Palette.Faint : token.Value.StartsWith('"') || token.Value.StartsWith('\'') ? Palette.StringInk : char.IsDigit(token.Value[0]) ? Palette.Amber : Palette.KeywordInk;
            ctx.DrawText(Format(token.Value, color), new Point(codeX + token.Index * CharWidth, y + 3)); pos = token.Index + token.Length;
        }
        if (pos < text.Length) ctx.DrawText(Format(text[pos..], Palette.Ink), new Point(codeX + pos * CharWidth, y + 3));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e); Focus(); int row = (int)(e.GetPosition(this).Y / LineHeight);
        if (row >= Rows.Count) return;
        if (Rows[row].Kind == ChangeKind.Fold) { ExpandRequested?.Invoke(); return; }
        _rightSide = e.GetPosition(this).X > Bounds.Width / 2; _selectStart = _selectEnd = row; e.Pointer.Capture(this); InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (e.Pointer.Captured == this) { _selectEnd = Math.Clamp((int)(e.GetPosition(this).Y / LineHeight), 0, Math.Max(0, Rows.Count - 1)); InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { e.Pointer.Capture(null); }
    protected override async void OnKeyDown(KeyEventArgs e) {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _selectStart >= 0) {
            var rows = Rows.Skip(Math.Min(_selectStart, _selectEnd)).Take(Math.Abs(_selectEnd - _selectStart) + 1);
            var text = string.Join(Environment.NewLine, rows.Select(r => Unified ? r.Kind == ChangeKind.Removed ? r.Left : r.Right ?? r.Left : _rightSide ? r.Right : r.Left).Where(s => s != null));
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

public sealed class ChangeMap : Control {
    public IReadOnlyList<DiffRow> Rows { get; set; } = [];
    public double ViewStart { get; set; }
    public double ViewEnd { get; set; }
    public event Action<double>? Navigate;
    public ChangeMap() { Width = 22; Cursor = new Cursor(StandardCursorType.Hand); }
    public override void Render(DrawingContext ctx) {
        ctx.FillRectangle(Palette.Bar, new Rect(Bounds.Size));
        if (Rows.Count == 0) return;
        for (int i = 0; i < Rows.Count; i++) if (Rows[i].Kind is not (ChangeKind.Equal or ChangeKind.Fold))
            ctx.FillRectangle(Rows[i].Kind == ChangeKind.Added ? Palette.Green : Rows[i].Kind == ChangeKind.Removed ? Palette.Red : Palette.Accent, new Rect(6, i * Bounds.Height / Rows.Count, 9, Math.Max(3, Bounds.Height / Rows.Count)));
        ctx.DrawRectangle(null, new Pen(Palette.Faint, 1), new Rect(1, ViewStart * Bounds.Height, 20, Math.Max(10, (ViewEnd - ViewStart) * Bounds.Height)));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) => Navigate?.Invoke(Math.Clamp(e.GetPosition(this).Y / Bounds.Height, 0, 1));
}
