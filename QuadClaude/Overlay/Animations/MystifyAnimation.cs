using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class MystifyAnimation : IIdleAnimation
{
    private const int RibbonCount = 2;
    private const int VerticesPerRibbon = 4;
    private const int TrailDepth = 12;

    private struct Vertex
    {
        public double X, Y, VX, VY;
    }

    private Vertex[][] _ribbons = [];
    private Polygon[][] _trails = [];
    private Point[][][] _trailHistory = [];
    private double _width, _height;
    private readonly Random _rng = new();

    private static readonly Color[] RibbonColors =
    {
        Color.FromArgb(0xCC, 0x00, 0xCC, 0xFF),
        Color.FromArgb(0xCC, 0xFF, 0x66, 0xAA),
    };

    public void Initialize(Canvas canvas, double width, double height)
    {
        _width = width;
        _height = height;

        _ribbons = new Vertex[RibbonCount][];
        _trails = new Polygon[RibbonCount][];
        _trailHistory = new Point[RibbonCount][][];

        for (int r = 0; r < RibbonCount; r++)
        {
            _ribbons[r] = new Vertex[VerticesPerRibbon];
            for (int v = 0; v < VerticesPerRibbon; v++)
            {
                _ribbons[r][v] = new Vertex
                {
                    X = _rng.NextDouble() * width,
                    Y = _rng.NextDouble() * height,
                    VX = (_rng.NextDouble() - 0.5) * 200,
                    VY = (_rng.NextDouble() - 0.5) * 200
                };
            }

            _trails[r] = new Polygon[TrailDepth];
            _trailHistory[r] = new Point[TrailDepth][];

            for (int t = 0; t < TrailDepth; t++)
            {
                double fade = 1.0 - (double)t / TrailDepth;
                var color = RibbonColors[r];
                var brush = new SolidColorBrush(Color.FromArgb(
                    (byte)(fade * 0.6 * 255), color.R, color.G, color.B));
                brush.Freeze();

                _trails[r][t] = new Polygon
                {
                    Stroke = brush,
                    StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)(fade * 0.08 * 255), color.R, color.G, color.B)),
                    Visibility = Visibility.Hidden
                };
                canvas.Children.Add(_trails[r][t]);

                _trailHistory[r][t] = new Point[VerticesPerRibbon];
            }
        }
    }

    public void Update(double deltaSeconds)
    {
        for (int r = 0; r < RibbonCount; r++)
        {
            // Shift trail history
            for (int t = TrailDepth - 1; t > 0; t--)
            {
                Array.Copy(_trailHistory[r][t - 1], _trailHistory[r][t], VerticesPerRibbon);
                if (_trails[r][t - 1].Visibility == Visibility.Visible)
                {
                    _trails[r][t].Points = new PointCollection(_trailHistory[r][t]);
                    _trails[r][t].Visibility = Visibility.Visible;
                }
            }

            // Move vertices
            for (int v = 0; v < VerticesPerRibbon; v++)
            {
                ref var vert = ref _ribbons[r][v];
                vert.X += vert.VX * deltaSeconds;
                vert.Y += vert.VY * deltaSeconds;

                if (vert.X <= 0) { vert.X = 0; vert.VX = Math.Abs(vert.VX); }
                else if (vert.X >= _width) { vert.X = _width; vert.VX = -Math.Abs(vert.VX); }

                if (vert.Y <= 0) { vert.Y = 0; vert.VY = Math.Abs(vert.VY); }
                else if (vert.Y >= _height) { vert.Y = _height; vert.VY = -Math.Abs(vert.VY); }

                _trailHistory[r][0][v] = new Point(vert.X, vert.Y);
            }

            _trails[r][0].Points = new PointCollection(_trailHistory[r][0]);
            _trails[r][0].Visibility = Visibility.Visible;
        }
    }

    public void Dispose() { }
}
