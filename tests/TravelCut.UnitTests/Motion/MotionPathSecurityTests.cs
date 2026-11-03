using System.Runtime.InteropServices;
using System.Text;
using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Motion;
using Xunit;

namespace TravelCut.Tests.Motion;

/// <summary>
/// Endurecimento de caminhos de asset (revisão de concorrência, M5): leitura de atributos sem seguir o link,
/// nomes curtos 8.3, curingas e junção quebrada. Usa junções reais (mklink /J) em diretórios temporários.
/// </summary>
public class MotionPathSecurityTests : IDisposable
{
    private readonly TempMotionProject _project = new();
    private readonly string _external = Path.Combine(Path.GetTempPath(), "tc-motion-sec-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = new();

    public MotionPathSecurityTests()
    {
        Directory.CreateDirectory(_external);
    }

    public void Dispose()
    {
        // Remove só os links (sem recursão), inclusive os quebrados, antes de apagar o projeto temporário.
        foreach (string link in _links)
            RemoveLinkOnly(link);

        _project.Dispose();

        try
        {
            if (Directory.Exists(_external))
                Directory.Delete(_external, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    private static MotionRenderRequestDto WithAsset(string asset) =>
        TempMotionProject.ValidRequest(
            parameters: new Dictionary<string, string> { ["title"] = "Veneza", ["backgroundAsset"] = asset });

    private void Junction(string link, string target)
    {
        TestJunction.Create(link, target);
        _links.Add(link);
    }

    private static void RemoveLinkOnly(string link)
    {
        try
        {
            if (Directory.Exists(link) || IsReparsePointEntry(link))
                Directory.Delete(link, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsReparsePointEntry(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Nome curto 8.3 do caminho, ou null se o volume não gerar nomes curtos para ele.</summary>
    private static string? ShortPathOf(string path)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
        if (length == 0 || length >= buffer.Capacity)
            return null;

        return buffer.ToString();
    }

    [Fact]
    public void ShortNameSegment_IsRejected_InAssetPath()
    {
        // Nome 8.3 sintético: nunca pode chegar ao resolvedor de asset, mesmo que exista um link por trás.
        Assert.False(MotionPaths.TryResolveAssetPath(_project.Root, "LINKEXT~1/foto.png", out _));
        Assert.False(MotionPaths.TryResolveAsset(_project.Root, "LINKEXT~1/foto.png", out _));

        var validation = MotionRequestValidator.Validate(WithAsset("LINKEXT~1/foto.png"), _project.Root);

        Assert.Equal(MotionRequestValidator.AssetOutsideAssetsCode, validation.Code);
    }

    [Fact]
    public void JunctionReachedByRealShortName_IsBlocked_WhenEightDotThreeIsAvailable()
    {
        File.WriteAllBytes(Path.Combine(_external, "foto.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        string link = Path.Combine(_project.AssetsRoot, "pasta longa de link");
        Junction(link, _external);

        string? shortLink = ShortPathOf(link);
        if (shortLink is null || string.Equals(Path.GetFileName(shortLink), Path.GetFileName(link), StringComparison.OrdinalIgnoreCase))
        {
            // Volume sem nomes 8.3: o caso fica coberto por ShortNameSegment_IsRejected_InAssetPath.
            Console.WriteLine("8.3 indisponível neste volume: teste de nome curto real não exercitado.");
            return;
        }

        // Comportamento verificado: o caminho pelo nome curto continua bloqueado. Observação: neste runtime o nome
        // 8.3 já chega expandido ao checador (Path.GetFullPath); este teste não discrimina implementações antigas.
        string viaShort = Path.Combine(_project.AssetsRoot, Path.GetFileName(shortLink), "foto.png");
        Assert.True(File.Exists(viaShort));
        Assert.True(MotionPaths.HasReparsePointBetween(_project.Root, viaShort));
    }

    [Theory]
    [InlineData("marca/*.png")]
    [InlineData("marca/fun?o.png")]
    [InlineData("*/fundo.png")]
    public void WildcardInAssetPath_IsRejected(string relative)
    {
        _project.AddAsset("marca/fundo.png");

        Assert.False(MotionPaths.TryResolveAssetPath(_project.Root, relative, out _));
        Assert.False(MotionPaths.TryResolveAsset(_project.Root, relative, out _));

        var validation = MotionRequestValidator.Validate(WithAsset(relative), _project.Root);

        Assert.Equal(MotionRequestValidator.AssetOutsideAssetsCode, validation.Code);
    }

    [Fact]
    public void BrokenJunction_InAssets_IsBlocked()
    {
        // Junção cujo alvo some depois de criada: o link continua existindo e deve bloquear o asset.
        string target = Path.Combine(Path.GetTempPath(), "tc-motion-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "foto.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        string link = Path.Combine(_project.AssetsRoot, "quebrada");
        Junction(link, target);
        Directory.Delete(target, recursive: true);

        // Assumido e verificado: atributos de junção quebrada ainda trazem ReparsePoint (sem exceção).
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);

        Assert.True(MotionPaths.HasReparsePointBetween(_project.Root, Path.Combine(link, "foto.png")));
        Assert.False(MotionPaths.TryResolveAsset(_project.Root, "quebrada/foto.png", out _));

        var validation = MotionRequestValidator.Validate(WithAsset("quebrada/foto.png"), _project.Root);

        Assert.Equal(MotionRequestValidator.PermissionDeniedCode, validation.Code);
        Assert.DoesNotContain(target, validation.Message);
    }

    [Fact]
    public void MissingSegment_IsNotReparsePoint_ControlCase()
    {
        // Segmento inexistente (render ainda não criado) não bloqueia: só os links bloqueiam.
        Assert.False(MotionPaths.HasReparsePointBetween(_project.Root,
            Path.Combine(_project.WorkRoot, "mr-00000000000000000000000000000000", MotionPaths.OutputFileName)));
    }
}
