using System.Numerics;

namespace TravelCut.Application.Editor;

/// <summary>
/// Mapeamento de taxas de quadros para o editor. Convenções:
/// - Timeline (<c>TimelineTimeBaseDto</c>): frameDuration = SEGUNDOS POR FRAME (num/den). Ex.: 1001/30000 → 29,97 fps.
/// - Fonte: taxa em frames POR SEGUNDO (num/den). Ex.: 30000/1001 → 29,97 fps.
/// Comparações são por valor racional (1001/24000 == 2002/48000).
/// </summary>
public static class EditorTimebases
{
    // (frameDurationNum, frameDurationDen, string do setting "timelineFrameRate" do Resolve)
    private static readonly (long Num, long Den, string Rate)[] TimelineRates =
    [
        (1001, 24000, "23.976"),
        (1, 24, "24"),
        (1, 25, "25"),
        (1001, 30000, "29.97"),
        (1, 30, "30"),
        (1, 50, "50"),
        (1001, 60000, "59.94"),
        (1, 60, "60"),
    ];

    // Taxas padrão (frames por segundo) usadas para reconhecer o FrameRate do ffprobe.
    private static readonly (long Num, long Den)[] StandardSourceRates =
    [
        (24000, 1001), (24, 1), (25, 1), (30000, 1001), (30, 1), (50, 1), (60000, 1001), (60, 1),
    ];

    private const double SourceRateToleranceFps = 0.05;

    /// <summary>String do Resolve para a base da timeline, ou null quando não suportada (inclui valores ≤ 0).</summary>
    public static string? TimelineFrameRate(long frameDurationNum, long frameDurationDen)
    {
        if (frameDurationNum <= 0 || frameDurationDen <= 0) return null;
        foreach (var (num, den, rate) in TimelineRates)
        {
            if (SameRate(frameDurationNum, frameDurationDen, num, den)) return rate;
        }
        return null;
    }

    /// <summary>
    /// Taxa da fonte (frames por segundo) a partir do FrameRate do ffprobe. A taxa padrão mais próxima (tolerância 0,05 fps)
    /// é usada; sem padrão, usa-se a taxa arredondada a milésimo. Valores não finitos ou ≤ 0 → null.
    /// </summary>
    public static (long Num, long Den)? SourceFpsFromFrameRate(double frameRate)
    {
        if (!double.IsFinite(frameRate) || frameRate <= 0) return null;

        (long Num, long Den)? best = null;
        double bestDiff = double.MaxValue;
        foreach (var (num, den) in StandardSourceRates)
        {
            double diff = Math.Abs((double)num / den - frameRate);
            if (diff < bestDiff) { bestDiff = diff; best = (num, den); }
        }
        if (best is not null && bestDiff <= SourceRateToleranceFps) return best;

        long milli = (long)Math.Round(frameRate * 1000, MidpointRounding.ToEven);
        return milli > 0 ? (milli, 1000) : null;
    }

    /// <summary>Igualdade racional de duas taxas (frames por segundo ou segundos por frame, conforme o par passado).</summary>
    public static bool SameRate(long aNum, long aDen, long bNum, long bDen) =>
        new BigInteger(aNum) * bDen == new BigInteger(bNum) * aDen;
}
