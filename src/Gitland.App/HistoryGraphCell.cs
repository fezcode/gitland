using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Gitland.Core;

namespace Gitland.App;

public sealed class HistoryGraphCell(HistoryGraphRow row) : Control {
    public override void Render(DrawingContext context) {
        double middle = Bounds.Height / 2;
        double X(int column) => 9 + column * 14;
        for (int i = 0; i < row.Incoming; i++) {
            if (i == row.Column && row.NewLane) continue;
            context.DrawLine(new Pen(Palette.Faint, 1), new Point(X(i), 0), new Point(X(i), middle));
        }
        foreach (var edge in row.Edges) {
            var pen = new Pen(Palette.Faint, 1);
            context.DrawLine(pen, new Point(X(edge.From), middle), new Point(X(edge.To), Bounds.Height - 8));
            context.DrawLine(pen, new Point(X(edge.To), Bounds.Height - 8), new Point(X(edge.To), Bounds.Height));
        }
        context.DrawEllipse(Palette.Ground, new Pen(Palette.Muted, 1.5), new Point(X(row.Column), middle), 3.5, 3.5);
    }
}
