using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuadClaude.Overlay.Animations;

public class GameOfLifeAnimation : IIdleAnimation
{
    private const double CellSize = 8;
    private const double StepInterval = 0.15;
    private const double InitialDensity = 0.3;

    private int _cols, _rows;
    private bool[,] _grid = new bool[0, 0];
    private bool[,] _next = new bool[0, 0];
    private Rectangle[,] _cells = new Rectangle[0, 0];
    private double _stepTimer;
    private int _lastPopulation;
    private int _stableFrames;
    private readonly Random _rng = new();

    private SolidColorBrush _aliveBrush = null!;
    private SolidColorBrush _deadBrush = null!;

    public void Initialize(Canvas canvas, double width, double height)
    {
        _cols = Math.Max(1, (int)(width / CellSize));
        _rows = Math.Max(1, (int)(height / CellSize));

        _aliveBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0xCC, 0x77));
        _aliveBrush.Freeze();
        _deadBrush = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        _deadBrush.Freeze();

        _grid = new bool[_cols, _rows];
        _next = new bool[_cols, _rows];
        _cells = new Rectangle[_cols, _rows];

        for (int x = 0; x < _cols; x++)
        {
            for (int y = 0; y < _rows; y++)
            {
                _grid[x, y] = _rng.NextDouble() < InitialDensity;

                _cells[x, y] = new Rectangle
                {
                    Width = CellSize - 1,
                    Height = CellSize - 1,
                    Fill = _grid[x, y] ? _aliveBrush : _deadBrush
                };

                Canvas.SetLeft(_cells[x, y], x * CellSize);
                Canvas.SetTop(_cells[x, y], y * CellSize);
                canvas.Children.Add(_cells[x, y]);
            }
        }
    }

    public void Update(double deltaSeconds)
    {
        _stepTimer += deltaSeconds;
        if (_stepTimer < StepInterval) return;
        _stepTimer = 0;

        int population = 0;

        for (int x = 0; x < _cols; x++)
        {
            for (int y = 0; y < _rows; y++)
            {
                int neighbors = CountNeighbors(x, y);
                _next[x, y] = _grid[x, y]
                    ? neighbors == 2 || neighbors == 3
                    : neighbors == 3;

                if (_next[x, y]) population++;
            }
        }

        // Swap and update visuals
        (_grid, _next) = (_next, _grid);

        for (int x = 0; x < _cols; x++)
            for (int y = 0; y < _rows; y++)
                _cells[x, y].Fill = _grid[x, y] ? _aliveBrush : _deadBrush;

        // Detect stagnation
        if (population == _lastPopulation)
            _stableFrames++;
        else
            _stableFrames = 0;

        _lastPopulation = population;

        if (population < 5 || _stableFrames > 20)
            Reseed();
    }

    private int CountNeighbors(int cx, int cy)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = (cx + dx + _cols) % _cols;
                int ny = (cy + dy + _rows) % _rows;
                if (_grid[nx, ny]) count++;
            }
        }
        return count;
    }

    private void Reseed()
    {
        _stableFrames = 0;
        for (int x = 0; x < _cols; x++)
            for (int y = 0; y < _rows; y++)
                _grid[x, y] = _rng.NextDouble() < InitialDensity;
    }

    public void Dispose() { }
}
