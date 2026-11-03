namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Mídias sintéticas e fixtures de porta por cenário. Silêncios de confiança "medium" (sempre revisão humana).
/// Geometria do cenário feliz (clip01, 12 s):
///   silêncio A 1,0–3,6 s (2,6 s, "silence/cut"); silêncio B 6,0–6,9 s (0,9 s, "pause/trim").
///   Fala em 0,0–1,0 e 3,6–4,6 e 6,9–7,5 s, portanto nenhum silêncio tem fala dentro da margem de ±0,5 s.
/// Geometria de clip02 (8 s): silêncio C 2,0–5,0 s (3,0 s).
/// </summary>
public static class E2EScenarioData
{
    public const string HappyClipA = "clip01.mp4";
    public const string HappyClipB = "clip02.mp4";

    public const string UnicodeClipA = "clipe ação 01.mp4";
    public const string UnicodeClipB = "cena 02 ✨.mov";

    public const string VfrClip = "vfr 01.mp4";
    public const string CfrClip = "cfr 02.mp4";

    public static readonly string[] HappyFiles = [HappyClipA, HappyClipB];
    public static readonly string[] UnicodeFiles = [UnicodeClipA, UnicodeClipB];
    public static readonly string[] VfrFiles = [VfrClip, CfrClip];

    /// <summary>Dois arquivos com transcrição e silêncios (fluxo feliz, reinício e cache).</summary>
    public static E2EPortFixtures Happy() => new E2EPortFixtures()
        .Add(new E2EMediaSpec(HappyClipA, E2EPortFixtures.Cfr2997(12),
            [
                E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"),
                E2EPortFixtures.Word(2, 0.5, 1.0, "pessoal"),
                E2EPortFixtures.Word(3, 3.6, 4.2, "chegamos"),
                E2EPortFixtures.Word(4, 4.2, 4.6, "hoje"),
                E2EPortFixtures.Word(5, 6.9, 7.1, "ao"),
                E2EPortFixtures.Word(6, 7.1, 7.5, "Rio"),
            ],
            [E2EPortFixtures.Silence(1.0, 3.6), E2EPortFixtures.Silence(6.0, 6.9)]))
        .Add(new E2EMediaSpec(HappyClipB, E2EPortFixtures.Cfr2997(8),
            [
                E2EPortFixtures.Word(1, 0.0, 0.4, "Bom"),
                E2EPortFixtures.Word(2, 0.4, 0.8, "dia"),
                E2EPortFixtures.Word(3, 5.0, 5.4, "vamos"),
                E2EPortFixtures.Word(4, 5.4, 5.7, "lá"),
            ],
            [E2EPortFixtures.Silence(2.0, 5.0)]));

    /// <summary>Mesma geometria do cenário feliz com nomes de arquivo com acento, espaço e emoji.</summary>
    public static E2EPortFixtures Unicode() => new E2EPortFixtures()
        .Add(new E2EMediaSpec(UnicodeClipA, E2EPortFixtures.Cfr2997(12),
            [
                E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"),
                E2EPortFixtures.Word(2, 0.5, 1.0, "pessoal"),
                E2EPortFixtures.Word(3, 3.6, 4.2, "chegamos"),
                E2EPortFixtures.Word(4, 4.2, 4.6, "hoje"),
                E2EPortFixtures.Word(5, 6.9, 7.1, "ao"),
                E2EPortFixtures.Word(6, 7.1, 7.5, "Rio"),
            ],
            [E2EPortFixtures.Silence(1.0, 3.6), E2EPortFixtures.Silence(6.0, 6.9)]))
        .Add(new E2EMediaSpec(UnicodeClipB, E2EPortFixtures.Cfr2997(8),
            [
                E2EPortFixtures.Word(1, 0.0, 0.4, "Bom"),
                E2EPortFixtures.Word(2, 0.4, 0.8, "dia"),
            ],
            [E2EPortFixtures.Silence(2.0, 5.0)]));

    /// <summary>Uma mídia VFR e uma CFR 29,97, ambas com transcrição e sem silêncios.</summary>
    public static E2EPortFixtures Vfr() => new E2EPortFixtures()
        .Add(new E2EMediaSpec(VfrClip, E2EPortFixtures.Vfr2997(12),
            [E2EPortFixtures.Word(1, 0.0, 0.5, "Olá"), E2EPortFixtures.Word(2, 0.5, 1.0, "tudo")],
            []))
        .Add(new E2EMediaSpec(CfrClip, E2EPortFixtures.Cfr2997(8),
            [E2EPortFixtures.Word(1, 0.0, 0.5, "Oi"), E2EPortFixtures.Word(2, 0.5, 1.0, "bem")],
            []));
}
