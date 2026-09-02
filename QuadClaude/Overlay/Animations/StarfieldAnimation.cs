using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class StarfieldAnimation : IIdleAnimation
{
    private const int StarCount = 120;
    private const double BaseSpeed = 180.0;

    private struct Star
    {
        public double X, Y, Z, Speed;
        public Ellipse Shape;
    }

    private Star[] _stars = [];
    private double _centerX, _centerY;
    private double _width, _height;
    private readonly Random _rng = new();

    public void Initialize(Canvas canvas, double width, double height)
    {
        _width = width;
        _height = height;
        _centerX = width / 2;
        _centerY = height / 2;
        _stars = new Star[StarCount];

        for (int i = 0; i < StarCount; i++)
        {
            var ellipse = new Ellipse { Fill = Brushes.White };
            canvas.Children.Add(ellipse);

            _stars[i] = new Star
            {
                X = _rng.NextDouble() * width,
                Y = _rng.NextDouble() * height,
                Z = _rng.NextDouble() * 0.9 + 0.1,
                Speed = _rng.NextDouble() * 0.6 + 0.4,
                Shape = ellipse
            };
        }
    }

    public void Update(double deltaSeconds)
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            ref var s = ref _stars[i];

            double dx = s.X - _centerX;
            double dy = s.Y - _centerY;
            double accel = s.Z * s.Speed * BaseSpeed * deltaSeconds;

            s.X += dx * accel / 100;
            s.Y += dy * accel / 100;
            s.Z += deltaSeconds * s.Speed * 0.4;

            if (s.Z > 1.0) s.Z = 1.0;

            if (s.X < -10 || s.X > _width + 10 || s.Y < -10 || s.Y > _height + 10)
            {
                s.X = _centerX + (_rng.NextDouble() - 0.5) * 60;
                s.Y = _centerY + (_rng.NextDouble() - 0.5) * 60;
                s.Z = _rng.NextDouble() * 0.2 + 0.05;
                s.Speed = _rng.NextDouble() * 0.6 + 0.4;
            }

            double size = 1.5 + s.Z * 3.5;
            s.Shape.Width = size;
            s.Shape.Height = size;
            s.Shape.Opacity = 0.2 + s.Z * 0.8;

            Canvas.SetLeft(s.Shape, s.X - size / 2);
            Canvas.SetTop(s.Shape, s.Y - size / 2);
        }
    }

    public void Dispose() { }
}
