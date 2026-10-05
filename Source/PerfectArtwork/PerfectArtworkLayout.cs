namespace Playhub.Integrations;

public sealed record PerfectArtworkLayout(double LogoX = 25, double LogoY = 50, double LogoScale = 100,
    double BackgroundX = 50, double BackgroundY = 50, double BackgroundScale = 100,
    double BackgroundOpacity = 100, double ShadowOpacity = 55, double ShadowBlur = 40, bool ShowLogo = true)
{
    public PerfectArtworkLayout Normalize() => new(Value(LogoX,25,0,100),Value(LogoY,50,0,100),Value(LogoScale,100,50,200),
        Value(BackgroundX,50,0,100),Value(BackgroundY,50,0,100),Value(BackgroundScale,100,100,200),
        Value(BackgroundOpacity,100,0,100),Value(ShadowOpacity,55,0,100),Value(ShadowBlur,40,0,100),ShowLogo);
    private static double Value(double value,double fallback,double minimum,double maximum) => double.IsFinite(value) ? Math.Clamp(value,minimum,maximum) : fallback;
}
