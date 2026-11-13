using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.App.Island;

/// <summary>
/// Hafif sönümlü yay eğrisi. Varsayılan sönüm (0.86) ≈ %0.7 aşma üretir: doğal ama zıplamasız.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn; // Eğriyi olduğu gibi kullan (EaseOut dönüşümü uygulama).
    }

    public double Damping { get; set; } = 0.86;

    /// <summary>ζ·ω: büyüdükçe daha hızlı oturur.</summary>
    public double Stiffness { get; set; } = 7.5;

    protected override double EaseInCore(double t)
    {
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        var value = Evaluate(t);
        var end = Evaluate(1);
        return value / end;
    }

    private double Evaluate(double t)
    {
        var zeta = Math.Clamp(Damping, 0.05, 0.99);
        var omega = Stiffness / zeta;
        var root = Math.Sqrt(1 - zeta * zeta);
        var wd = omega * root;
        return 1 - Math.Exp(-zeta * omega * t) * (Math.Cos(wd * t) + zeta / root * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
