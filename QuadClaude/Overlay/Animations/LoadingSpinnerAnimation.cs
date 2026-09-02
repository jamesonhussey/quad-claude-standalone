using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class LoadingSpinnerAnimation : IIdleAnimation
{
    private const int DotCount = 10;
    private const double Radius = 28;
    private const double RotationSpeed = 2.5;

    private Ellipse[] _dots = [];
    private double _centerX, _centerY;
    private double _angle;
    private SolidColorBrush[] _brushes = [];

    public void Initialize(Canvas canvas, double width, double height)
    {
        _centerX = width / 2;
        _centerY = height / 2;

        _brushes = new SolidColorBrush[DotCount];
        for (int i = 0; i < DotCount; i++)
        {
            double fade = 1.0 - (double)i / DotCount;
            byte a = (byte)(fade * 200 + 55);
            _brushes[i] = new SolidColorBrush(Color.FromArgb(a, 0xEE, 0xEE, 0xF0));
            _brushes[i].Freeze();
        }

        _dots = new Ellipse[DotCount];
        for (int i = 0; i < DotCount; i++)
        {
            double size = 4 + (1.0 - (double)i / DotCount) * 4;
            _dots[i] = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = _brushes[i]
            };
            canvas.Children.Add(_dots[i]);
        }
    }

    public void Update(double deltaSeconds)
    {
        _angle += RotationSpeed * deltaSeconds;

        for (int i = 0; i < DotCount; i++)
        {
            double dotAngle = _angle - i * (Math.PI * 2 / DotCount);
            double x = _centerX + Math.Cos(dotAngle) * Radius - _dots[i].Width / 2;
            double y = _centerY + Math.Sin(dotAngle) * Radius - _dots[i].Height / 2;

            Canvas.SetLeft(_dots[i], x);
            Canvas.SetTop(_dots[i], y);
        }
    }

    public void Dispose() { }
}
