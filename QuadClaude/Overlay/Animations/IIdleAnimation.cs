using System.Windows.Controls;

namespace QuadClaude.Overlay.Animations;

public interface IIdleAnimation : IDisposable
{
    void Initialize(Canvas canvas, double width, double height);
    void Update(double deltaSeconds);
}
