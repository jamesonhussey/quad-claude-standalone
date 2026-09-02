using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class TesseractAnimation : IIdleAnimation
{
    private const int VertexCount = 16;
    private const int EdgeCount = 32;
    private const double Scale = 80;
    private const double Dist4D = 2.5;
    private const double Dist3D = 4.0;

    private static readonly double[][] Vertices;
    private static readonly (int A, int B)[] Edges;

    private Line[] _lines = [];
    private double _centerX, _centerY;
    private double _angleXW, _angleYZ, _angleXY, _angleZW;

    static TesseractAnimation()
    {
        Vertices = new double[VertexCount][];
        int idx = 0;
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                for (int c = -1; c <= 1; c += 2)
                    for (int d = -1; d <= 1; d += 2)
                        Vertices[idx++] = [a, b, c, d];

        var edges = new List<(int, int)>();
        for (int i = 0; i < VertexCount; i++)
            for (int j = i + 1; j < VertexCount; j++)
            {
                int diff = 0;
                for (int k = 0; k < 4; k++)
                    if (Vertices[i][k] != Vertices[j][k]) diff++;
                if (diff == 1) edges.Add((i, j));
            }
        Edges = edges.ToArray();
    }

    public void Initialize(Canvas canvas, double width, double height)
    {
        _centerX = width / 2;
        _centerY = height / 2;

        var brush = new SolidColorBrush(Color.FromArgb(0xBB, 0x00, 0xCC, 0xFF));
        brush.Freeze();

        _lines = new Line[EdgeCount];
        for (int i = 0; i < EdgeCount; i++)
        {
            _lines[i] = new Line
            {
                Stroke = brush,
                StrokeThickness = 1.2
            };
            canvas.Children.Add(_lines[i]);
        }

        var dotBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0x00, 0xEE, 0xFF));
        dotBrush.Freeze();
        for (int i = 0; i < VertexCount; i++)
        {
            var dot = new Ellipse { Width = 4, Height = 4, Fill = dotBrush };
            canvas.Children.Add(dot);
        }
    }

    public void Update(double deltaSeconds)
    {
        _angleXW += 0.7 * deltaSeconds;
        _angleYZ += 0.5 * deltaSeconds;
        _angleXY += 0.3 * deltaSeconds;
        _angleZW += 0.4 * deltaSeconds;

        var projected = new double[VertexCount][];

        for (int i = 0; i < VertexCount; i++)
        {
            double x = Vertices[i][0], y = Vertices[i][1];
            double z = Vertices[i][2], w = Vertices[i][3];

            // Rotate XW
            double cosA = Math.Cos(_angleXW), sinA = Math.Sin(_angleXW);
            double nx = x * cosA - w * sinA;
            double nw = x * sinA + w * cosA;
            x = nx; w = nw;

            // Rotate YZ
            double cosB = Math.Cos(_angleYZ), sinB = Math.Sin(_angleYZ);
            double ny = y * cosB - z * sinB;
            double nz = y * sinB + z * cosB;
            y = ny; z = nz;

            // Rotate XY
            double cosC = Math.Cos(_angleXY), sinC = Math.Sin(_angleXY);
            nx = x * cosC - y * sinC;
            ny = x * sinC + y * cosC;
            x = nx; y = ny;

            // Rotate ZW
            double cosD = Math.Cos(_angleZW), sinD = Math.Sin(_angleZW);
            nz = z * cosD - w * sinD;
            nw = z * sinD + w * cosD;
            z = nz; w = nw;

            // 4D → 3D perspective
            double s4 = Dist4D / (Dist4D - w);
            double x3 = x * s4, y3 = y * s4, z3 = z * s4;

            // 3D → 2D perspective
            double s3 = Dist3D / (Dist3D - z3);
            projected[i] = [
                _centerX + x3 * s3 * Scale,
                _centerY + y3 * s3 * Scale,
                s3 * s4
            ];
        }

        for (int i = 0; i < EdgeCount; i++)
        {
            var (a, b) = Edges[i];
            _lines[i].X1 = projected[a][0];
            _lines[i].Y1 = projected[a][1];
            _lines[i].X2 = projected[b][0];
            _lines[i].Y2 = projected[b][1];

            double depth = (projected[a][2] + projected[b][2]) / 2;
            _lines[i].Opacity = Math.Clamp(depth * 0.5, 0.15, 1.0);
        }

        // Update vertex dots
        var canvas = _lines[0]?.Parent as Canvas;
        if (canvas == null) return;

        int dotStart = EdgeCount;
        for (int i = 0; i < VertexCount; i++)
        {
            if (dotStart + i >= canvas.Children.Count) break;
            var dot = canvas.Children[dotStart + i] as Ellipse;
            if (dot == null) continue;

            Canvas.SetLeft(dot, projected[i][0] - 2);
            Canvas.SetTop(dot, projected[i][1] - 2);
            dot.Opacity = Math.Clamp(projected[i][2] * 0.6, 0.2, 1.0);
        }
    }

    public void Dispose() { }
}
