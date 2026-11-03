using System.Globalization;
using System.Numerics;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Domain.Planning;

/// <summary>
/// Aritmética temporal exata do plano editorial.
/// Convenções: fonte em microssegundos (PTS, [in,out)); timeline em frames inteiros;
/// <see cref="RationalTimeBase"/> = segundos POR FRAME (Numerator/Denominator, ex.: 1001/30000).
/// Todo cálculo é feito em BigInteger (racional exato); arredondamento é SEMPRE half-even (bancário).
/// duração exata (frames) = (outUs-inUs)·den·speedDen / (1e6·num·speedNum).
/// </summary>
public static class TimelineMath
{
    private const long MicrosPerSecond = 1_000_000;

    /// <summary>Duração isolada de um intervalo de fonte na timeline, em frames (half-even).</summary>
    public static long DurationToFrames(long durationUs, int speedNum, int speedDen, RationalTimeBase tb)
    {
        ValidateTb(tb);
        ValidateSpeed(speedNum, speedDen);
        if (durationUs < 0) throw new ArgumentOutOfRangeException(nameof(durationUs));
        var (n, d) = ExactFrames(durationUs, speedNum, speedDen, tb);
        return (long)RoundHalfEven(n, d);
    }

    /// <summary>
    /// Posiciona segmentos em sequência. A fronteira k é round_half_even(startFrame + soma EXATA das durações racionais
    /// dos segmentos anteriores), logo não há drift: o erro absoluto de qualquer fronteira é ≤ 0,5 frame.
    /// A duração de cada segmento é a diferença entre fronteiras (pode ser 0 se o segmento for &lt; 0,5 frame).
    /// Segmento com out &lt; in lança ArgumentException; out == in resulta em duração 0.
    /// </summary>
    public static IReadOnlyList<(long StartFrame, long DurationFrames)> LayoutSequential(
        IReadOnlyList<(long InUs, long OutUs, int SpeedNum, int SpeedDen)> segments, RationalTimeBase tb, long startFrame = 0)
    {
        ValidateTb(tb);
        var result = new List<(long, long)>(segments.Count);
        BigInteger accN = BigInteger.Zero, accD = BigInteger.One;
        long prevBoundary = startFrame;
        foreach (var s in segments)
        {
            if (s.OutUs < s.InUs) throw new ArgumentException("Segmento com out < in.", nameof(segments));
            ValidateSpeed(s.SpeedNum, s.SpeedDen);
            var (n, d) = ExactFrames(s.OutUs - s.InUs, s.SpeedNum, s.SpeedDen, tb);
            accN = accN * d + n * accD;
            accD *= d;
            var g = BigInteger.GreatestCommonDivisor(accN, accD);
            if (!g.IsOne && !g.IsZero) { accN /= g; accD /= g; }
            long boundary = (long)RoundHalfEven(accN + startFrame * accD, accD);
            result.Add((prevBoundary, boundary - prevBoundary));
            prevBoundary = boundary;
        }
        return result;
    }

    /// <summary>
    /// Índice de frame da FONTE (inteiro) de um instante em µs, para taxa fpsNum/fpsDen (frames POR SEGUNDO, ex.: 30000/1001).
    /// round_half_even(us · fpsNum / (fpsDen · 1e6)). Exposto para o Application montar o manifesto de edição.
    /// </summary>
    public static long MicrosecondsToFrameIndex(long us, long fpsNum, long fpsDen)
    {
        if (fpsNum <= 0 || fpsDen <= 0) throw new ArgumentOutOfRangeException(nameof(fpsNum), "Taxa deve ser > 0.");
        return (long)RoundHalfEven(new BigInteger(us) * fpsNum, new BigInteger(MicrosPerSecond) * fpsDen);
    }

    /// <summary>frames → µs (half-even). Determinístico; usado só para exibição.</summary>
    public static long FramesToMicroseconds(long frames, RationalTimeBase tb)
    {
        ValidateTb(tb);
        return (long)RoundHalfEven(new BigInteger(frames) * tb.Numerator * MicrosPerSecond, tb.Denominator);
    }

    /// <summary>
    /// Timecode HH:MM:SS:FF NÃO-drop-frame. FPS nominal = round(1/frameDuration) (29,97→30, 23,976→24, 59,94→60).
    /// Em taxas NTSC o timecode NDF diverge do relógio real (~3,6 s/h); é apenas rótulo de frames, não tempo de parede.
    /// Frames negativos recebem prefixo '-'.
    /// </summary>
    public static string FormatTimecode(long frames, RationalTimeBase tb)
    {
        ValidateTb(tb);
        long nominal = Math.Max(1, (long)RoundHalfEven(tb.Denominator, tb.Numerator));
        bool neg = frames < 0;
        long abs = neg ? -frames : frames;
        long ff = abs % nominal;
        long totalSec = abs / nominal;
        long ss = totalSec % 60, mm = totalSec / 60 % 60, hh = totalSec / 3600;
        var s = string.Create(CultureInfo.InvariantCulture, $"{hh:00}:{mm:00}:{ss:00}:{ff:00}");
        return neg ? "-" + s : s;
    }

    private static readonly (double Fps, RationalTimeBase Tb)[] Candidates =
    {
        (24000.0 / 1001, RationalTimeBase.Fps23976),
        (24, RationalTimeBase.Fps24),
        (25, RationalTimeBase.Fps25),
        (30000.0 / 1001, RationalTimeBase.Fps2997),
        (30, RationalTimeBase.Fps30),
        (50, new RationalTimeBase(1, 50)),
        (60000.0 / 1001, RationalTimeBase.Fps5994),
        (60, RationalTimeBase.Fps60),
    };

    /// <summary>
    /// Moda das taxas conhecidas mapeadas ao racional NTSC/inteiro mais próximo (tolerância 0,05 fps).
    /// Valores fora da tolerância (ex.: VFR médio 29,83), nulos, NaN ou ≤0 são ignorados.
    /// Empate ou nenhuma taxa válida → 1001/30000.
    /// </summary>
    public static RationalTimeBase ChooseTimelineTimeBase(IEnumerable<double?> mediaFps)
    {
        var counts = new int[Candidates.Length];
        foreach (var f in mediaFps)
        {
            if (f is not double v || double.IsNaN(v) || double.IsInfinity(v) || v <= 0) continue;
            int best = -1; double bestDiff = 0.05 + 1e-9;
            for (int i = 0; i < Candidates.Length; i++)
            {
                double diff = Math.Abs(Candidates[i].Fps - v);
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }
            if (best >= 0) counts[best]++;
        }
        int max = counts.Max();
        if (max == 0 || counts.Count(c => c == max) != 1) return RationalTimeBase.Fps2997;
        return Candidates[Array.IndexOf(counts, max)].Tb;
    }

    internal static (BigInteger N, BigInteger D) ExactFrames(long durationUs, int speedNum, int speedDen, RationalTimeBase tb)
    {
        var n = new BigInteger(durationUs) * tb.Denominator * speedDen;
        var d = new BigInteger(MicrosPerSecond) * tb.Numerator * speedNum;
        return (n, d);
    }

    /// <summary>Divisão arredondada half-even; d &gt; 0.</summary>
    internal static BigInteger RoundHalfEven(BigInteger n, BigInteger d)
    {
        var q = BigInteger.DivRem(n, d, out var r);
        if (r.Sign < 0) { q -= 1; r += d; } // floor
        var twice = r * 2;
        int cmp = twice.CompareTo(d);
        if (cmp < 0) return q;
        if (cmp > 0) return q + 1;
        return q.IsEven ? q : q + 1;
    }

    private static void ValidateTb(RationalTimeBase tb)
    {
        if (tb.Numerator <= 0 || tb.Denominator <= 0) throw new ArgumentException("Timebase inválido.", nameof(tb));
    }

    private static void ValidateSpeed(int num, int den)
    {
        if (num <= 0 || den <= 0) throw new ArgumentOutOfRangeException(nameof(num), "Velocidade deve ser > 0.");
    }
}
