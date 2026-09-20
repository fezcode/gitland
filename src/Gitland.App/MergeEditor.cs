using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Gitland.Core;

namespace Gitland.App;

/// <summary>Native editing with a display-only layer behind the text presenter.</summary>
public sealed class MergeEditor : TextBox {
    public static FontFeatureCollection LiteralCodeFeatures => [FontFeature.Parse("liga=0"), FontFeature.Parse("clig=0"), FontFeature.Parse("calt=0")];
    public static readonly IBrush OursInk = Palette.OursInk, TheirsInk = Palette.TheirsInk;
    static readonly IBrush OursFill = Palette.OursFill, TheirsFill = Palette.TheirsFill, BaseFill = Palette.BaseFill, MarkerFill = Palette.MarkerFill, ResolvedFill = Palette.AddedFill;
    MergeLineLayer? _lineLayer;
    IReadOnlyList<MergeLineHighlight> _highlights = [];
    internal TextPresenter? Presenter { get; private set; }
    public int LineNumberStart { get; set; } = 1;
    public int ActiveStart { get; private set; } = -1;
    public int ActiveLength { get; private set; }
    public event Action? ViewChanged;
    public IReadOnlyList<MergeLineHighlight> Highlights => _highlights;
    protected override Type StyleKeyOverride => typeof(TextBox);
    public MergeEditor() {
        Classes.Add("merge-editor");
        FontFamily = Palette.Mono; FontSize = 13; LineHeight = 23;
        FontFeatures = LiteralCodeFeatures;
        Foreground = Palette.Ink; Background = Palette.Ground;
        CaretBrush = Palette.Ink; SelectionBrush = Palette.Selection;
        SelectionForegroundBrush = Palette.Ink;
        AcceptsReturn = true; AcceptsTab = true; IsUndoEnabled = true;
        TextWrapping = TextWrapping.NoWrap; BorderThickness = new Thickness(0);
        Padding = new Thickness(14, 10); VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;
    }
    public void RefreshAppearance() { _lineLayer?.InvalidateVisual(); ViewChanged?.Invoke(); }
    public void SetHighlights(IReadOnlyList<MergeLineHighlight> highlights) { _highlights = highlights; RefreshAppearance(); }
    public void SetActiveRange(int start, int length) { ActiveStart = start; ActiveLength = length; ViewChanged?.Invoke(); }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        var presenter = Presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        if (presenter?.GetVisualParent() is Panel panel) {
            _lineLayer = new MergeLineLayer(this, presenter) { IsHitTestVisible = false };
            panel.Children.Insert(0, _lineLayer);
        }
    }
    public static IBrush Fill(MergeLineKind kind) => kind switch { MergeLineKind.Ours => OursFill, MergeLineKind.Theirs => TheirsFill, MergeLineKind.Base => BaseFill, MergeLineKind.Marker => MarkerFill, _ => ResolvedFill };
    static IBrush Edge(MergeLineKind kind) => kind switch { MergeLineKind.Ours => OursInk, MergeLineKind.Theirs => TheirsInk, MergeLineKind.Base => Palette.Faint, MergeLineKind.Marker => Palette.Amber, _ => Palette.Green };

    sealed class MergeLineLayer(MergeEditor editor, TextPresenter presenter) : Control {
        public override void Render(DrawingContext context) {
            var highlights = editor.Highlights;
            if (highlights.Count == 0) return;
            double y = 0; int at = 0;
            foreach (var line in presenter.TextLayout.TextLines) {
                while (at < highlights.Count && highlights[at].Start + highlights[at].Length <= line.FirstTextSourceIndex) at++;
                if (at < highlights.Count && highlights[at].Start < line.FirstTextSourceIndex + line.Length) {
                    var kind = highlights[at].Kind;
                    context.FillRectangle(Fill(kind), new Rect(0, y, Bounds.Width, line.Height));
                    context.FillRectangle(Edge(kind), new Rect(0, y, 2, line.Height));
                }
                y += line.Height;
            }
        }
    }
}
