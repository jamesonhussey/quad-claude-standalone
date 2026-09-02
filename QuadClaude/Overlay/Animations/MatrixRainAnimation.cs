using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuadClaude.Overlay.Animations;

public class MatrixRainAnimation : IIdleAnimation
{
    private const double ColumnWidth = 14;
    private const int MaxTrailLength = 16;
    private const double CharFlickerInterval = 0.1;

    private struct Column
    {
        public double Y;
        public double Speed;
        public double Delay;
        public TextBlock[] Trail;
        public double FlickerTimer;
    }

    private Column[] _columns = [];
    private double _height;
    private readonly Random _rng = new();
    private SolidColorBrush[] _trailBrushes = [];

    private static readonly char[] CharSet = BuildCharSet();

    private static char[] BuildCharSet()
    {
        var chars = new List<char>();
        for (char c = 'ｦ'; c <= 'ﾝ'; c++) chars.Add(c);
        for (char c = '0'; c <= '9'; c++) chars.Add(c);
        for (char c = 'A'; c <= 'Z'; c++) chars.Add(c);
        return chars.ToArray();
    }

    public void Initialize(Canvas canvas, double width, double height)
    {
        _height = height;

        _trailBrushes = new SolidColorBrush[MaxTrailLength];
        for (int i = 0; i < MaxTrailLength; i++)
        {
            double fade = 1.0 - (double)i / MaxTrailLength;
            byte g = (byte)(0xFF * fade);
            byte r = (byte)(0x00 * fade);
            byte b = (byte)(0x44 * fade);
            byte a = (byte)(0xFF * fade * 0.9);
            _trailBrushes[i] = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            _trailBrushes[i].Freeze();
        }

        int colCount = Math.Max(1, (int)(width / ColumnWidth));
        _columns = new Column[colCount];

        var font = new FontFamily("Cascadia Code,Consolas");

        for (int c = 0; c < colCount; c++)
        {
            var trail = new TextBlock[MaxTrailLength];
            for (int t = 0; t < MaxTrailLength; t++)
            {
                trail[t] = new TextBlock
                {
                    Text = CharSet[_rng.Next(CharSet.Length)].ToString(),
                    FontFamily = font,
                    FontSize = 12,
                    Foreground = _trailBrushes[t],
                    Visibility = Visibility.Hidden
                };
                Canvas.SetLeft(trail[t], c * ColumnWidth);
                canvas.Children.Add(trail[t]);
            }

            _columns[c] = new Column
            {
                Y = -_rng.NextDouble() * height,
                Speed = 60 + _rng.NextDouble() * 140,
                Delay = _rng.NextDouble() * 3.0,
                Trail = trail,
                FlickerTimer = 0
            };
        }
    }

    public void Update(double deltaSeconds)
    {
        for (int c = 0; c < _columns.Length; c++)
        {
            ref var col = ref _columns[c];

            if (col.Delay > 0)
            {
                col.Delay -= deltaSeconds;
                continue;
            }

            col.Y += col.Speed * deltaSeconds;
            col.FlickerTimer += deltaSeconds;

            bool flicker = col.FlickerTimer >= CharFlickerInterval;
            if (flicker) col.FlickerTimer = 0;

            for (int t = 0; t < MaxTrailLength; t++)
            {
                double ty = col.Y - t * 14;
                if (ty < -14 || ty > _height)
                {
                    col.Trail[t].Visibility = Visibility.Hidden;
                    continue;
                }

                col.Trail[t].Visibility = Visibility.Visible;
                Canvas.SetTop(col.Trail[t], ty);

                if (t == 0 && flicker)
                    col.Trail[t].Text = CharSet[_rng.Next(CharSet.Length)].ToString();
                else if (t > 0 && flicker && _rng.NextDouble() < 0.1)
                    col.Trail[t].Text = CharSet[_rng.Next(CharSet.Length)].ToString();
            }

            if (col.Y - MaxTrailLength * 14 > _height)
            {
                col.Y = -_rng.NextDouble() * _height * 0.5;
                col.Speed = 60 + _rng.NextDouble() * 140;
                col.Delay = _rng.NextDouble() * 2.0;

                foreach (var tb in col.Trail)
                {
                    tb.Visibility = Visibility.Hidden;
                    tb.Text = CharSet[_rng.Next(CharSet.Length)].ToString();
                }
            }
        }
    }

    public void Dispose() { }
}
