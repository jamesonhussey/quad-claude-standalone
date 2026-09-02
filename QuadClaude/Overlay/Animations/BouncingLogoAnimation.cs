using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuadClaude.Overlay.Animations;

public class BouncingLogoAnimation : IIdleAnimation
{
    private double _x, _y, _vx, _vy;
    private double _width, _height;
    private Border? _logo;
    private TextBlock? _text;
    private readonly Random _rng = new();

    private static readonly Color[] Colors =
    {
        Color.FromRgb(0x00, 0xCC, 0xFF),
        Color.FromRgb(0xFF, 0x66, 0xAA),
        Color.FromRgb(0x00, 0xFF, 0x88),
        Color.FromRgb(0xFF, 0xB3, 0x47),
        Color.FromRgb(0xBB, 0x88, 0xFF),
        Color.FromRgb(0xFF, 0x55, 0x55),
        Color.FromRgb(0x44, 0x88, 0xFF),
    };

    public void Initialize(Canvas canvas, double width, double height)
    {
        _width = width;
        _height = height;

        _text = new TextBlock
        {
            Text = "CLAUDE",
            FontSize = 28,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors[_rng.Next(Colors.Length)])
        };

        _logo = new Border
        {
            Child = _text,
            BorderThickness = new Thickness(2),
            BorderBrush = _text.Foreground,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6)
        };

        canvas.Children.Add(_logo);

        _logo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double logoW = _logo.DesiredSize.Width;
        double logoH = _logo.DesiredSize.Height;

        _x = _rng.NextDouble() * (width - logoW);
        _y = _rng.NextDouble() * (height - logoH);

        double speed = 100 + _rng.NextDouble() * 60;
        double angle = _rng.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(angle) * speed;
        _vy = Math.Sin(angle) * speed;

        Canvas.SetLeft(_logo, _x);
        Canvas.SetTop(_logo, _y);
    }

    public void Update(double deltaSeconds)
    {
        if (_logo == null || _text == null) return;

        double logoW = _logo.DesiredSize.Width;
        double logoH = _logo.DesiredSize.Height;

        _x += _vx * deltaSeconds;
        _y += _vy * deltaSeconds;

        bool bounced = false;

        if (_x <= 0) { _x = 0; _vx = Math.Abs(_vx); bounced = true; }
        else if (_x + logoW >= _width) { _x = _width - logoW; _vx = -Math.Abs(_vx); bounced = true; }

        if (_y <= 0) { _y = 0; _vy = Math.Abs(_vy); bounced = true; }
        else if (_y + logoH >= _height) { _y = _height - logoH; _vy = -Math.Abs(_vy); bounced = true; }

        if (bounced)
        {
            var color = Colors[_rng.Next(Colors.Length)];
            var brush = new SolidColorBrush(color);
            _text.Foreground = brush;
            _logo.BorderBrush = brush;
        }

        Canvas.SetLeft(_logo, _x);
        Canvas.SetTop(_logo, _y);
    }

    public void Dispose() { }
}
