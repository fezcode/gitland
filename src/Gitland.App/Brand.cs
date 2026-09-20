using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Gitland.App;

public static partial class Palette {
    /// <summary>A Git graph rooted in the contours of an island.</summary>
    public static Control Logo(double size = 28) {
        var canvas = new Canvas { Width = 32, Height = 32 };
        void Path(string data, IBrush stroke, double width, IBrush? fill = null) => canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = width, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Fill = fill });
        Path("M3 20L16 14L29 20L16 26Z", Green, 1.4, AddedFill);
        Path("M3 24L16 30L29 24", Green, 1.4);
        Path("M10 18L10 6M10 15C10 10 22 15 22 9L22 7", Accent, 2.2);
        foreach (var (x, y) in new[] { (10d, 5d), (22d, 6d), (10d, 18d) }) {
            var node = new Ellipse { Width = 5, Height = 5, Fill = Bar, Stroke = Accent, StrokeThickness = 1.7 }; Canvas.SetLeft(node, x - 2.5); Canvas.SetTop(node, y - 2.5); canvas.Children.Add(node);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
