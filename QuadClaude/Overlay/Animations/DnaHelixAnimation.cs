using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class DnaHelixAnimation : IIdleAnimation
{
    private const int Rungs = 20;
    private const double HelixRadius = 50;
    private const double VerticalSpacing = 18;
    private const double RotationSpeed = 1.8;

    private Ellipse[] _dotsA = [];
    private Ellipse[] _dotsB = [];
    private Line[] _basePairs = [];
    private Line[] _backboneA = [];
    private Line[] _backboneB = [];
    private double _centerX, _centerY;
    private double _rotation;

    private static readonly SolidColorBrush CyanBrush;
    private static readonly SolidColorBrush MagentaBrush;
    private static readonly SolidColorBrush PairBrush;
    private static readonly SolidColorBrush BackboneBrushA;
    private static readonly SolidColorBrush BackboneBrushB;

    static DnaHelixAnimation()
    {
        CyanBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0x00, 0xDD, 0xFF));
        CyanBrush.Freeze();
        MagentaBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0x44, 0xCC));
        MagentaBrush.Freeze();
        PairBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        PairBrush.Freeze();
        BackboneBrushA = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0xDD, 0xFF));
        BackboneBrushA.Freeze();
        BackboneBrushB = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x44, 0xCC));
        BackboneBrushB.Freeze();
    }

    public void Initialize(Canvas canvas, double width, double height)
    {
        _centerX = width / 2;
        _centerY = height / 2;

        _dotsA = new Ellipse[Rungs];
        _dotsB = new Ellipse[Rungs];
        _basePairs = new Line[Rungs];
        _backboneA = new Line[Rungs - 1];
        _backboneB = new Line[Rungs - 1];

        for (int i = 0; i < Rungs - 1; i++)
        {
            _backboneA[i] = new Line { Stroke = BackboneBrushA, StrokeThickness = 1.5 };
            _backboneB[i] = new Line { Stroke = BackboneBrushB, StrokeThickness = 1.5 };
            canvas.Children.Add(_backboneA[i]);
            canvas.Children.Add(_backboneB[i]);
        }

        for (int i = 0; i < Rungs; i++)
        {
            _basePairs[i] = new Line { Stroke = PairBrush, StrokeThickness = 1 };
            canvas.Children.Add(_basePairs[i]);

            _dotsA[i] = new Ellipse { Width = 7, Height = 7, Fill = CyanBrush };
            _dotsB[i] = new Ellipse { Width = 7, Height = 7, Fill = MagentaBrush };
            canvas.Children.Add(_dotsA[i]);
            canvas.Children.Add(_dotsB[i]);
        }
    }

    public void Update(double deltaSeconds)
    {
        _rotation += RotationSpeed * deltaSeconds;

        double startY = _centerY - (Rungs - 1) * VerticalSpacing / 2;

        double[] axArr = new double[Rungs], ayArr = new double[Rungs];
        double[] bxArr = new double[Rungs], byArr = new double[Rungs];

        for (int i = 0; i < Rungs; i++)
        {
            double angle = _rotation + i * 0.55;
            double y = startY + i * VerticalSpacing;

            double cosA = Math.Cos(angle);
            double cosB = Math.Cos(angle + Math.PI);

            double depthA = (Math.Sin(angle) + 1) / 2;
            double depthB = (Math.Sin(angle + Math.PI) + 1) / 2;

            double ax = _centerX + cosA * HelixRadius;
            double bx = _centerX + cosB * HelixRadius;

            axArr[i] = ax; ayArr[i] = y;
            bxArr[i] = bx; byArr[i] = y;

            double sizeA = 5 + depthA * 4;
            double sizeB = 5 + depthB * 4;

            _dotsA[i].Width = sizeA;
            _dotsA[i].Height = sizeA;
            _dotsA[i].Opacity = 0.4 + depthA * 0.6;
            Canvas.SetLeft(_dotsA[i], ax - sizeA / 2);
            Canvas.SetTop(_dotsA[i], y - sizeA / 2);

            _dotsB[i].Width = sizeB;
            _dotsB[i].Height = sizeB;
            _dotsB[i].Opacity = 0.4 + depthB * 0.6;
            Canvas.SetLeft(_dotsB[i], bx - sizeB / 2);
            Canvas.SetTop(_dotsB[i], y - sizeB / 2);

            _basePairs[i].X1 = ax;
            _basePairs[i].Y1 = y;
            _basePairs[i].X2 = bx;
            _basePairs[i].Y2 = y;
            _basePairs[i].Opacity = Math.Min(depthA, depthB) * 0.6 + 0.15;
        }

        for (int i = 0; i < Rungs - 1; i++)
        {
            _backboneA[i].X1 = axArr[i];
            _backboneA[i].Y1 = ayArr[i];
            _backboneA[i].X2 = axArr[i + 1];
            _backboneA[i].Y2 = ayArr[i + 1];

            _backboneB[i].X1 = bxArr[i];
            _backboneB[i].Y1 = byArr[i];
            _backboneB[i].X2 = bxArr[i + 1];
            _backboneB[i].Y2 = byArr[i + 1];
        }
    }

    public void Dispose() { }
}
