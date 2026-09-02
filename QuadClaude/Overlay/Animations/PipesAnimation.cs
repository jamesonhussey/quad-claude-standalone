using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class PipesAnimation : IIdleAnimation
{
    private const double CellSize = 24;
    private const double GrowInterval = 0.1;
    private const double FillThreshold = 0.7;
    private const int MaxPipes = 4;

    private enum Direction { Up, Down, Left, Right }

    private struct PipeHead
    {
        public int X, Y;
        public Direction Dir;
        public bool Active;
    }

    private int _cols, _rows;
    private bool[,] _occupied = new bool[0, 0];
    private Canvas? _canvas;
    private double _growTimer;
    private int _filledCells;
    private PipeHead[] _pipes = new PipeHead[MaxPipes];
    private readonly Random _rng = new();

    private static readonly SolidColorBrush[] PipeColors;

    static PipesAnimation()
    {
        PipeColors = new SolidColorBrush[]
        {
            Freeze(Color.FromArgb(0xCC, 0x00, 0xCC, 0xFF)),
            Freeze(Color.FromArgb(0xCC, 0x00, 0xFF, 0x88)),
            Freeze(Color.FromArgb(0xCC, 0xFF, 0xB3, 0x47)),
            Freeze(Color.FromArgb(0xCC, 0xFF, 0x66, 0xAA)),
        };

        static SolidColorBrush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    public void Initialize(Canvas canvas, double width, double height)
    {
        _canvas = canvas;
        _cols = Math.Max(2, (int)(width / CellSize));
        _rows = Math.Max(2, (int)(height / CellSize));
        _occupied = new bool[_cols, _rows];

        for (int i = 0; i < MaxPipes; i++)
            _pipes[i] = SpawnPipe();
    }

    public void Update(double deltaSeconds)
    {
        _growTimer += deltaSeconds;
        if (_growTimer < GrowInterval) return;
        _growTimer = 0;

        if (_filledCells > _cols * _rows * FillThreshold)
            ClearAndRestart();

        for (int i = 0; i < MaxPipes; i++)
        {
            ref var pipe = ref _pipes[i];
            if (!pipe.Active)
            {
                pipe = SpawnPipe();
                continue;
            }

            DrawSegment(pipe.X, pipe.Y, PipeColors[i % PipeColors.Length]);
            _occupied[pipe.X, pipe.Y] = true;
            _filledCells++;

            if (!TryAdvance(ref pipe))
            {
                pipe = SpawnPipe();
                if (!IsValid(pipe.X, pipe.Y))
                    pipe.Active = false;
            }
        }
    }

    private PipeHead SpawnPipe()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = _rng.Next(_cols);
            int y = _rng.Next(_rows);
            if (!_occupied[x, y])
            {
                return new PipeHead
                {
                    X = x,
                    Y = y,
                    Dir = (Direction)_rng.Next(4),
                    Active = true
                };
            }
        }
        return new PipeHead { Active = false };
    }

    private bool TryAdvance(ref PipeHead pipe)
    {
        if (_rng.NextDouble() < 0.25)
            pipe.Dir = (Direction)_rng.Next(4);

        var dirs = new[] { pipe.Dir, (Direction)_rng.Next(4), (Direction)_rng.Next(4), (Direction)_rng.Next(4) };
        foreach (var dir in dirs)
        {
            var (nx, ny) = Step(pipe.X, pipe.Y, dir);
            if (IsValid(nx, ny) && !_occupied[nx, ny])
            {
                pipe.X = nx;
                pipe.Y = ny;
                pipe.Dir = dir;
                return true;
            }
        }
        return false;
    }

    private static (int, int) Step(int x, int y, Direction dir) => dir switch
    {
        Direction.Up => (x, y - 1),
        Direction.Down => (x, y + 1),
        Direction.Left => (x - 1, y),
        Direction.Right => (x + 1, y),
        _ => (x, y)
    };

    private bool IsValid(int x, int y) => x >= 0 && x < _cols && y >= 0 && y < _rows;

    private void DrawSegment(int gx, int gy, SolidColorBrush brush)
    {
        if (_canvas == null) return;

        var rect = new Rectangle
        {
            Width = CellSize - 2,
            Height = CellSize - 2,
            Fill = brush,
            RadiusX = 3,
            RadiusY = 3,
            Opacity = 0.8
        };

        Canvas.SetLeft(rect, gx * CellSize + 1);
        Canvas.SetTop(rect, gy * CellSize + 1);
        _canvas.Children.Add(rect);
    }

    private void ClearAndRestart()
    {
        _canvas?.Children.Clear();
        _occupied = new bool[_cols, _rows];
        _filledCells = 0;

        for (int i = 0; i < MaxPipes; i++)
            _pipes[i] = SpawnPipe();
    }

    public void Dispose() { }
}
