namespace QuadClaude.Overlay.Animations;

public static class AnimationFactory
{
    private static readonly Random Rng = new();

    public static IIdleAnimation CreateRandom()
    {
        return Rng.Next(10) switch
        {
            0 => new StarfieldAnimation(),
            1 => new MatrixRainAnimation(),
            2 => new MystifyAnimation(),
            3 => new BouncingLogoAnimation(),
            4 => new GameOfLifeAnimation(),
            5 => new SineWaveAnimation(),
            6 => new PipesAnimation(),
            7 => new TesseractAnimation(),
            8 => new DnaHelixAnimation(),
            _ => new RadarSweepAnimation(),
        };
    }
}
