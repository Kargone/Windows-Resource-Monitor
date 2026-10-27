using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PowerMonitor.UI;

/// <summary>Draws the app's lightning-bolt icon at runtime so no image asset is needed.</summary>
public static class IconFactory
{
    public static BitmapSource CreateBolt(int size = 64)
    {
        // Lightning bolt described in a 0..1 unit box, then scaled to the requested size.
        (double x, double y)[] pts =
        {
            (0.56, 0.04), (0.26, 0.56), (0.46, 0.56),
            (0.40, 0.96), (0.78, 0.38), (0.54, 0.38),
        };

        var figure = new PathFigure { StartPoint = new Point(pts[0].x * size, pts[0].y * size), IsClosed = true };
        for (int i = 1; i < pts.Length; i++)
            figure.Segments.Add(new LineSegment(new Point(pts[i].x * size, pts[i].y * size), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xD1, 0x66),
            Color.FromRgb(0xFF, 0x9F, 0x1C),
            new Point(0, 0), new Point(1, 1));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawGeometry(fill, null, geometry);

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
