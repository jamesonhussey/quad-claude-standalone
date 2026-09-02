using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class SineWaveAnimation : IIdleAnimation
{
    private const int WaveCount = 4;
    private const int PointsPerWave = 80;

    private struct WaveParams
    {
        public double Frequency;
        public double Amplitude;
        public double PhaseSpeed;
        public double Phase;
        public double VerticalOffset;
    }

    private WaveParams[] _waves = [];
    private Polygon[] _polygons = [];
    private double _width, _height;
    private double _time;
    private readonly Random _rng = new();

    private static readonly Color[] WaveColors =
    {
        Color.FromArgb(0x44, 0x00, 0xCC, 0xFF),
        Color.FromArgb(0x44, 0xBB, 0x88, 0xFF),
        Color.FromArgb(0x44, 0x00, 0xFF, 0x88),
        Color.FromArgb(0x44, 0xFF, 0xB3, 0x47),
    };

    private static readonly Color[] StrokeColors =
    {
        Color.FromArgb(0x99, 0x00, 0xCC, 0xFF),
        Color.FromArgb(0x99, 0xBB, 0x88, 0xFF),
        Color.FromArgb(0x99, 0x00, 0xFF, 0x88),
        Color.FromArgb(0x99, 0xFF, 0xB3, 0x47),
    };

    public void Initialize(Canvas canvas, double width, double height)
    {
        _width = width;
        _height = height;

        _waves = new WaveParams[WaveCount];
        _polygons = new Polygon[WaveCount];

        for (int i = 0; i < WaveCount; i++)
        {
            _waves[i] = new WaveParams
            {
                Frequency = 0.008 + _rng.NextDouble() * 0.012,
                Amplitude = height * (0.08 + _rng.NextDouble() * 0.12),
                PhaseSpeed = 0.8 + _rng.NextDouble() * 1.2,
                Phase = _rng.NextDouble() * Math.PI * 2,
                VerticalOffset = height * (0.3 + i * 0.12)
            };

            var fillBrush = new SolidColorBrush(WaveColors[i]);
            fillBrush.Freeze();
            var strokeBrush = new SolidColorBrush(StrokeColors[i]);
            strokeBrush.Freeze();

            _polygons[i] = new Polygon
            {
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = 1.5
            };
            canvas.Children.Add(_polygons[i]);
        }
    }

    public void Update(double deltaSeconds)
    {
        _time += deltaSeconds;

        for (int w = 0; w < WaveCount; w++)
        {
            ref var wave = ref _waves[w];
            wave.Phase += wave.PhaseSpeed * deltaSeconds;

            var points = new PointCollection(PointsPerWave + 2);
            double step = _width / (PointsPerWave - 1);

            for (int i = 0; i < PointsPerWave; i++)
            {
                double x = i * step;
                double y = wave.VerticalOffset
                    + Math.Sin(x * wave.Frequency + wave.Phase) * wave.Amplitude
                    + Math.Sin(x * wave.Frequency * 0.5 + wave.Phase * 0.7) * wave.Amplitude * 0.4;
                points.Add(new Point(x, y));
            }

            points.Add(new Point(_width, _height));
            points.Add(new Point(0, _height));

            _polygons[w].Points = points;
        }
    }

    public void Dispose() { }
}
