using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Gitland.Core;

namespace Gitland.App;

public sealed class HistoryGraphCell : Control {
    public const int LaneSpacing = 20;
    public static readonly StyledProperty<double> EmphasisProperty = AvaloniaProperty.Register<HistoryGraphCell, double>(nameof(Emphasis));
    public double Emphasis { get => GetValue(EmphasisProperty); set => SetValue(EmphasisProperty, value); }
    readonly HistoryGraphRow _row;
    public int Lane => _row.Lane;
    public bool IsMerge => _row.Edges.Count(e => e.From == _row.Column) > 1;
    static HistoryGraphCell() => AffectsRender<HistoryGraphCell>(EmphasisProperty);
    public HistoryGraphCell(HistoryGraphRow row) {
        _row = row; IsHitTestVisible = false;
        Motion.Bind(this, reduced => Transitions = reduced ? null : new Transitions { new DoubleTransition { Property = EmphasisProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() } });
    }
    public static IBrush LaneBrush(int lane) => (lane % 5) switch { 0 => Palette.OursInk, 1 => Palette.TheirsInk, 2 => Palette.Green, 3 => Palette.Amber, _ => Palette.Accent };
    public override void Render(DrawingContext context) {
        double middle = Bounds.Height / 2;
        double X(int column) => 12 + column * LaneSpacing;
        for (int i = 0; i < _row.Incoming; i++) {
            if (i == _row.Column && _row.NewLane) continue;
            int lane = i < _row.IncomingLanes.Count ? _row.IncomingLanes[i] : 0;
            context.DrawLine(new Pen(LaneBrush(lane), 1.8), new Point(X(i), 0), new Point(X(i), middle));
        }
        for (int i = 0; i < _row.Edges.Count; i++) {
            var edge = _row.Edges[i]; var pen = new Pen(LaneBrush(i < _row.EdgeLanes.Count ? _row.EdgeLanes[i] : 0), 1.8);
            var geometry = new StreamGeometry();
            using (var path = geometry.Open()) {
                path.BeginFigure(new Point(X(edge.From), middle), false);
                path.CubicBezierTo(new Point(X(edge.From), Bounds.Height * .86), new Point(X(edge.To), Bounds.Height * .66), new Point(X(edge.To), Bounds.Height));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
        }
        var center = new Point(X(_row.Column), middle); var color = LaneBrush(_row.Lane);
        if (Emphasis > 0) {
            using (context.PushOpacity(.15 * Emphasis)) context.DrawEllipse(color, null, center, 10, 10);
            using (context.PushOpacity(Emphasis)) context.DrawEllipse(null, new Pen(color, 1), center, 8, 8);
        }
        context.DrawEllipse(Palette.Ground, new Pen(color, 1.8), center, IsMerge ? 5 : 3.8, IsMerge ? 5 : 3.8);
        if (IsMerge || Emphasis > .1) context.DrawEllipse(color, null, center, 2.1, 2.1);
    }
}
