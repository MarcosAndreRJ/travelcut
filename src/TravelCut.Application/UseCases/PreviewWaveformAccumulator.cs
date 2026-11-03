namespace TravelCut.Application.UseCases;

/// <summary>
/// Acumula picos absolutos por bucket a partir de PCM mono s16le (little-endian) lido em blocos.
/// Pico = max(|amostra|) / 32768, limitado a 1. Amostras são associadas ao bucket pela posição temporal:
/// bucket = floor(índice / (taxa * duração / buckets)). Funciona com blocos de tamanho arbitrário
/// (um byte órfão de um bloco é guardado para o próximo).
/// </summary>
public sealed class PreviewWaveformAccumulator
{
    public const double FullScale = 32768.0;

    private readonly double _samplesPerBucket;
    private readonly int _buckets;
    private readonly double[] _peaks;
    private readonly byte[] _carry = new byte[1];
    private int _carryCount;
    private long _sampleIndex;

    /// <param name="buckets">Número de buckets (16..2000 no contrato).</param>
    /// <param name="sampleRate">Taxa de amostragem do stream.</param>
    /// <param name="durationSeconds">Duração coberta pelo stream (end - start); deve ser &gt; 0.</param>
    public PreviewWaveformAccumulator(int buckets, int sampleRate, double durationSeconds)
    {
        if (buckets <= 0) throw new ArgumentOutOfRangeException(nameof(buckets));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!(durationSeconds > 0) || !double.IsFinite(durationSeconds)) throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        _buckets = buckets;
        _peaks = new double[buckets];
        _samplesPerBucket = sampleRate * durationSeconds / buckets;
    }

    /// <summary>Total de amostras processadas (para verificação de "sem áudio").</summary>
    public long SamplesProcessed => _sampleIndex;

    /// <summary>Alimenta um bloco de bytes PCM s16le.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        int offset = 0;

        if (_carryCount == 1 && data.Length > 0)
        {
            short sample = (short)(_carry[0] | (data[0] << 8));
            AddSample(sample);
            _carryCount = 0;
            offset = 1;
        }

        int remaining = data.Length - offset;
        int whole = remaining / 2;
        for (int i = 0; i < whole; i++)
        {
            int p = offset + i * 2;
            short sample = (short)(data[p] | (data[p + 1] << 8));
            AddSample(sample);
        }

        if ((remaining & 1) == 1)
        {
            _carry[0] = data[data.Length - 1];
            _carryCount = 1;
        }
    }

    /// <summary>Lê o stream inteiro (até EOF) em blocos de 64 KiB.</summary>
    public async Task ConsumeAsync(Stream stream, CancellationToken ct = default)
    {
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            Feed(buffer.AsSpan(0, read));
        }
    }

    /// <summary>Picos finais normalizados 0..1 (um por bucket; buckets sem amostras ficam em 0).</summary>
    public List<double> ToPeaks()
    {
        var list = new List<double>(_buckets);
        for (int i = 0; i < _buckets; i++)
        {
            list.Add(Math.Min(1.0, _peaks[i]));
        }
        return list;
    }

    private void AddSample(short sample)
    {
        // Math.Abs(short.MinValue) estoura em short; usa int.
        int abs = Math.Abs((int)sample);
        double value = abs / FullScale;

        int bucket = (int)(_sampleIndex / _samplesPerBucket);
        if (bucket >= _buckets) bucket = _buckets - 1;
        if (value > _peaks[bucket]) _peaks[bucket] = value;

        _sampleIndex++;
    }
}
