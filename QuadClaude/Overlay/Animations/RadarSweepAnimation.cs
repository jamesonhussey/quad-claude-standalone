using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class RadarSweepAnimation : IIdleAnimation
{
    private const double SweepSpeed = 1.5;
    private const int RingCount = 4;
    private const int MaxBlips = 12;
    private const double BlipLifetime = 4.0;

    private struct Blip
    {
        public double X, Y, Age;
        public bool Active;
        public Ellipse Shape;
    }

    private double _centerX, _centerY, _radius;
    private double _sweepAngle;
    private Line _sweepLine = null!;
    private Blip[] _blips = [];
    private readonly Random _rng = new();
    private double _blipTimer;

    private static readonly SolidColorBrush SweepBrush;
    private static readonly SolidColorBrush RingBrush;
    private static readonly SolidColorBrush CrossBrush;
    private static readonly SolidColorBrush BlipBrush;
    private static readonly SolidColorBrush CenterBrush;

    static RadarSweepAnimation()
    {
        SweepBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0xFF, 0x66));
        SweepBrush.Freeze();
        RingBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xFF, 0x66));
        RingBrush.Freeze();
        CrossBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0xFF, 0x66));
        CrossBrush.Freeze();
        BlipBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0x00, 0xFF, 0x88));
        BlipBrush.Freeze();
        CenterBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0xFF, 0x66));
        CenterBrush.Freeze();
    }

    public void Initialize(Canvas canvas, double width, double height)
    {
        _centerX = width / 2;
        _centerY = height / 2;
        _radius = Math.Min(width, height) * 0.38;

        // Distance rings
        for (int i = 1; i <= RingCount; i++)
        {
            double r = _radius * i / RingCount;
            var ring = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = RingBrush, StrokeThickness = 1,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(ring, _centerX - r);
            Canvas.SetTop(ring, _centerY - r);
            canvas.Children.Add(ring);
        }

        // Crosshairs
        canvas.Children.Add(new Line
        {
            X1 = _centerX - _radius, Y1 = _centerY,
            X2 = _centerX + _radius, Y2 = _centerY,
            Stroke = CrossBrush, StrokeThickness = 1
        });
        canvas.Children.Add(new Line
        {
            X1 = _centerX, Y1 = _centerY - _radius,
            X2 = _centerX, Y2 = _centerY + _radius,
            Stroke = CrossBrush, StrokeThickness = 1
        });

        // Center dot
        var center = new Ellipse { Width = 6, Height = 6, Fill = CenterBrush };
        Canvas.SetLeft(center, _centerX - 3);
        Canvas.SetTop(center, _centerY - 3);
        canvas.Children.Add(center);

        // Sweep line
        _sweepLine = new Line
        {
            X1 = _centerX, Y1 = _centerY,
            Stroke = SweepBrush, StrokeThickness = 2
        };
        canvas.Children.Add(_sweepLine);

        // Sweep trail (fading lines behind the sweep)
        for (int i = 1; i <= 8; i++)
        {
            double fade = 1.0 - i / 9.0;
            var trailBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(fade * 0x44), 0x00, 0xFF, 0x66));
            trailBrush.Freeze();

            var trail = new Line
            {
                X1 = _centerX, Y1 = _centerY,
                Stroke = trailBrush, StrokeThickness = 1.5,
                Tag = $"trail_{i}"
            };
            canvas.Children.Add(trail);
        }

        // Blips
        _blips = new Blip[MaxBlips];
        for (int i = 0; i < MaxBlips; i++)
        {
            _blips[i].Shape = new Ellipse
            {
                Width = 6, Height = 6,
                Fill = BlipBrush,
                Visibility = Visibility.Hidden
            };
            canvas.Children.Add(_blips[i].Shape);
        }
    }

    public void Update(double deltaSeconds)
    {
        _sweepAngle += SweepSpeed * deltaSeconds;
        _blipTimer += deltaSeconds;

        // Update sweep line
        double endX = _centerX + Math.Cos(_sweepAngle) * _radius;
        double endY = _centerY + Math.Sin(_sweepAngle) * _radius;
        _sweepLine.X2 = endX;
        _sweepLine.Y2 = endY;

        // Update sweep trail
        var canvas = _sweepLine.Parent as Canvas;
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is Line line && line.Tag is string tag && tag.StartsWith("trail_"))
            {
                int idx = int.Parse(tag[6..]);
                double trailAngle = _sweepAngle - idx * 0.06;
                line.X2 = _centerX + Math.Cos(trailAngle) * _radius;
                line.Y2 = _centerY + Math.Sin(trailAngle) * _radius;
            }
        }

        // Spawn blips
        if (_blipTimer > 0.4)
        {
            _blipTimer = 0;
            if (_rng.NextDouble() < 0.5)
                SpawnBlip();
        }

        // Update blips
        for (int i = 0; i < MaxBlips; i++)
        {
            if (!_blips[i].Active) continue;

            _blips[i].Age += deltaSeconds;
            if (_blips[i].Age > BlipLifetime)
            {
                _blips[i].Active = false;
                _blips[i].Shape.Visibility = Visibility.Hidden;
                continue;
            }

            double fade = 1.0 - _blips[i].Age / BlipLifetime;
            _blips[i].Shape.Opacity = fade;

            double pulse = 4 + Math.Sin(_blips[i].Age * 4) * 2;
            _blips[i].Shape.Width = pulse;
            _blips[i].Shape.Height = pulse;
            Canvas.SetLeft(_blips[i].Shape, _blips[i].X - pulse / 2);
            Canvas.SetTop(_blips[i].Shape, _blips[i].Y - pulse / 2);
        }
    }

    private void SpawnBlip()
    {
        for (int i = 0; i < MaxBlips; i++)
        {
            if (_blips[i].Active) continue;

            double angle = _rng.NextDouble() * Math.PI * 2;
            double dist = _rng.NextDouble() * _radius * 0.85 + _radius * 0.1;

            _blips[i].X = _centerX + Math.Cos(angle) * dist;
            _blips[i].Y = _centerY + Math.Sin(angle) * dist;
            _blips[i].Age = 0;
            _blips[i].Active = true;
            _blips[i].Shape.Visibility = Visibility.Visible;
            _blips[i].Shape.Opacity = 1.0;
            break;
        }
    }

    public void Dispose() { }
}
