using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Polle.Api.Features.Drought;

public sealed class DroughtDataUnavailableException(string message) : Exception(message);

public sealed record DroughtClass(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("percentile_max")] double PercentileMax,
    [property: JsonPropertyName("color")] string Color);

public sealed record DroughtLevel(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("color")] string Color);

public sealed record DroughtSource(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);

public sealed record DroughtIndexInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("window_days")] int WindowDays,
    [property: JsonPropertyName("base_period")] string BasePeriod);

public sealed record TrendModelInfo(
    [property: JsonPropertyName("window_days")] int WindowDays,
    [property: JsonPropertyName("rain_factor_limits")] double[] RainFactorLimits,
    [property: JsonPropertyName("percentile_scale")] int PercentileScale,
    [property: JsonPropertyName("not_rated")] int NotRated,
    [property: JsonPropertyName("status_names")] string[] StatusNames,
    [property: JsonPropertyName("method")] string Method);

public sealed record TrendArray(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dtype")] string Dtype,
    [property: JsonPropertyName("shape")] int[] Shape,
    [property: JsonPropertyName("scale")] double Scale,
    [property: JsonPropertyName("offset")] long Offset,
    [property: JsonPropertyName("bytes")] long Bytes);

public sealed record TrendManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("fit_years")] int[] FitYears,
    [property: JsonPropertyName("centre_year")] double CentreYear,
    [property: JsonPropertyName("first_month")] string FirstMonth,
    [property: JsonPropertyName("last_month")] string LastMonth,
    [property: JsonPropertyName("projected_from")] string ProjectedFrom,
    [property: JsonPropertyName("image_bounds")] double[][] ImageBounds,
    [property: JsonPropertyName("model")] TrendModelInfo Model,
    [property: JsonPropertyName("index")] DroughtIndexInfo Index,
    [property: JsonPropertyName("layers")] JsonElement Layers,
    [property: JsonPropertyName("classes")] DroughtClass[] Classes,
    [property: JsonPropertyName("no_drought")] DroughtLevel? NoDrought,
    [property: JsonPropertyName("sources")] DroughtSource[] Sources,
    [property: JsonPropertyName("arrays")] TrendArray[] Arrays);

/// <summary>Values of one land cell for one month, from the trend model. Null where the cell has no data.</summary>
public sealed record TrendCell(string Month, string Status, double? Percentile, int DroughtClass,
    double? TmeanC, double? TminC, double? TmaxC, double? Precipitation30dMm, double? Et030dMm, double? WaterBalance30dMm);

/// <summary>
/// The climate trend model written by pipelines/drought (cli.py trend) into ClimateTrend/: monthly norms of the
/// last decade and one yearly trend per land cell. It ships with the API, so any month of the timeline is
/// computed here instead of being read from daily files. It never changes while the process runs.
/// </summary>
public sealed class ClimateTrendStore(IConfiguration configuration, IWebHostEnvironment environment) : IDisposable
{
    public const int SchemaVersion = 1;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly MemoryCache images = new(new MemoryCacheOptions { SizeLimit = 64 });
    private TrendModel? model;

    private string Directory => Path.GetFullPath(configuration["Drought:DataDirectory"] ?? "ClimateTrend", environment.ContentRootPath);

    public async Task<TrendModel> GetModelAsync(CancellationToken ct)
    {
        if (model is { } loaded) return loaded;
        await gate.WaitAsync(ct);
        try { return model ??= await TrendModel.LoadAsync(Directory, ct); }
        finally { gate.Release(); }
    }

    /// <summary>The month's overlay of a grid layer as a PNG, or null when the model has no such layer.</summary>
    public async Task<byte[]?> GetImageAsync(int month, string layer, CancellationToken ct)
    {
        var current = await GetModelAsync(ct);
        if (images.TryGetValue((month, layer), out byte[]? cached)) return cached;
        var codes = current.Codes(month, layer, out var colors);
        if (codes is null) return null;
        var png = PalettePng.Encode(MercatorOverlay.Paint(current.Cells, codes), MercatorOverlay.Size, MercatorOverlay.Size, colors);
        images.Set((month, layer), png, new MemoryCacheEntryOptions { Size = 1 });
        return png;
    }

    public void Dispose()
    {
        images.Dispose();
        gate.Dispose();
    }
}

public sealed class TrendModel
{
    private const int Columns = 720;
    private readonly int firstYear;
    private readonly int cellCount;
    private readonly float[] tmaxNorm, tminNorm, precipNorm, et0Norm, et0Trend, tmaxTrend, tminTrend, precipTrend;
    private readonly byte[] status, percentile;

    private TrendModel(TrendManifest manifest, int[] cells, IReadOnlyDictionary<string, Array> arrays)
    {
        Manifest = manifest;
        Cells = cells;
        cellCount = cells.Length;
        firstYear = ParseMonth(manifest.FirstMonth).Year;
        var last = ParseMonth(manifest.LastMonth);
        MonthCount = (last.Year - firstYear) * 12 + last.Month;
        tmaxNorm = (float[])arrays["tmax_norm"]; tminNorm = (float[])arrays["tmin_norm"];
        precipNorm = (float[])arrays["precip_norm"]; et0Norm = (float[])arrays["et0_norm"]; et0Trend = (float[])arrays["et0_trend"];
        tmaxTrend = (float[])arrays["tmax_trend"]; tminTrend = (float[])arrays["tmin_trend"]; precipTrend = (float[])arrays["precip_trend"];
        status = (byte[])arrays["status"]; percentile = (byte[])arrays["percentile"];
        if (percentile.Length != MonthCount * cellCount || status.Length != 12 * cellCount)
            throw new DroughtDataUnavailableException("The climate trend model does not match its timeline.");
    }

    public TrendManifest Manifest { get; }
    /// <summary>Land cell indexes (row * 720 + column), ascending.</summary>
    public int[] Cells { get; }
    public int MonthCount { get; }

    public string MonthName(int month) => $"{firstYear + month / 12:D4}-{month % 12 + 1:D2}";

    /// <summary>Position of a YYYY-MM month in the timeline, or null outside it.</summary>
    public int? MonthIndex(string value)
    {
        if (!DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        var index = (date.Year - firstYear) * 12 + date.Month - 1;
        return index >= 0 && index < MonthCount ? index : null;
    }

    public static int CellIndex(double latitude, double longitude)
    {
        var row = Math.Min((int)Math.Floor((90 - latitude) / 0.5), 359);
        var column = (int)Math.Floor(((longitude + 180) % 360 + 360) % 360 / 0.5);
        return row * Columns + Math.Min(column, Columns - 1);
    }

    /// <summary>Position of the cell containing a point in <see cref="Cells"/>, or null over sea.</summary>
    public int? CellRow(double latitude, double longitude)
    {
        var row = Array.BinarySearch(Cells, CellIndex(latitude, longitude));
        return row < 0 ? null : row;
    }

    public TrendCell Cell(int month, int row)
    {
        var offset = firstYear + month / 12 - Manifest.CentreYear;
        var at = month % 12 * cellCount + row;
        var tmax = tmaxNorm[at] + tmaxTrend[row] * offset;
        var tmin = tminNorm[at] + tminTrend[row] * offset;
        var limits = Manifest.Model.RainFactorLimits;
        var window = Manifest.Model.WindowDays;
        var rain = precipNorm[at] * Math.Clamp(1 + precipTrend[row] * offset, limits[0], limits[1]) * window;
        var et0 = Math.Max(et0Norm[at] + et0Trend[at] * offset, 0) * window;
        var stored = percentile[month * cellCount + row];
        double? rank = stored == Manifest.Model.NotRated ? null : stored / (double)Manifest.Model.PercentileScale;
        return new(MonthName(month), Manifest.Model.StatusNames[status[at]], rank, rank is { } value ? ClassFor(value) : -1,
            Round((tmax + tmin) / 2), Round(tmin), Round(tmax), Round(rain), Round(et0), Round(rain - et0));
    }

    /// <summary>Palette code per cell for a grid layer (0 transparent) and the colours of codes 1..n.</summary>
    public byte[]? Codes(int month, string layer, out string[] colors)
    {
        var codes = new byte[cellCount];
        if (layer == "drought")
        {
            colors = [.. Manifest.Classes.Select(item => item.Color), Manifest.NoDrought?.Color ?? "#b7d9a8"];
            for (var row = 0; row < cellCount; row++)
            {
                if (status[month % 12 * cellCount + row] != 0) continue;
                var stored = percentile[month * cellCount + row];
                var level = stored == Manifest.Model.NotRated ? -1 : ClassFor(stored / (double)Manifest.Model.PercentileScale);
                codes[row] = (byte)(level >= 0 ? level + 1 : Manifest.Classes.Length + 1);
            }
            return codes;
        }
        var bins = BinsFor(layer);
        if (bins is null) { colors = []; return null; }
        colors = bins.Value.Colors;
        for (var row = 0; row < cellCount; row++)
        {
            var cell = Cell(month, row);
            var value = layer == "temperature" ? cell.TmeanC : cell.Precipitation30dMm;
            // Same rule as the pipeline's bin_codes: the number of upper edges at or below the value, plus one.
            if (value is { } number) codes[row] = (byte)(bins.Value.Edges.Count(edge => edge <= number) + 1);
        }
        return codes;
    }

    private (double[] Edges, string[] Colors)? BinsFor(string layer)
    {
        foreach (var item in Manifest.Layers.EnumerateArray())
        {
            if (item.GetProperty("id").GetString() != layer || !item.TryGetProperty("edges", out var edges)) continue;
            return ([.. edges.EnumerateArray().Select(edge => edge.GetDouble())],
                [.. item.GetProperty("legend").EnumerateArray().Select(entry => entry.GetProperty("color").GetString()!)]);
        }
        return null;
    }

    /// <summary>The tightest drought class whose percentile limit the value is within, or -1.</summary>
    private int ClassFor(double rank)
    {
        var level = -1;
        for (var k = 0; k < Manifest.Classes.Length; k++)
            if (rank <= Manifest.Classes[k].PercentileMax) level = k;
        return level;
    }

    private static double? Round(double value) => double.IsFinite(value) ? Math.Round(value, 1) : null;

    private static DateOnly ParseMonth(string value) =>
        DateOnly.ParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<TrendModel> LoadAsync(string directory, CancellationToken ct)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var blobPath = Path.Combine(directory, "model.bin");
        if (!File.Exists(manifestPath) || !File.Exists(blobPath))
            throw new DroughtDataUnavailableException("The climate trend model has not been built yet.");
        TrendManifest manifest;
        await using (var stream = File.OpenRead(manifestPath))
            manifest = await JsonSerializer.DeserializeAsync<TrendManifest>(stream, cancellationToken: ct)
                ?? throw new DroughtDataUnavailableException("The climate trend manifest is empty.");
        if (manifest.SchemaVersion != ClimateTrendStore.SchemaVersion)
            throw new DroughtDataUnavailableException($"Climate trend schema {manifest.SchemaVersion} is not supported.");
        var blob = await File.ReadAllBytesAsync(blobPath, ct);
        var arrays = new Dictionary<string, Array>();
        foreach (var entry in manifest.Arrays)
        {
            if (entry.Offset < 0 || entry.Offset + entry.Bytes > blob.Length)
                throw new DroughtDataUnavailableException($"The climate trend array {entry.Name} is truncated.");
            arrays[entry.Name] = Decode(blob.AsSpan((int)entry.Offset, (int)entry.Bytes), entry);
        }
        return new(manifest, (int[])arrays["cells"], arrays);
    }

    /// <summary>Little-endian integers to values: bytes and cell indexes as stored, scaled ones as floats with NaN where missing.</summary>
    private static Array Decode(ReadOnlySpan<byte> data, TrendArray entry)
    {
        switch (entry.Dtype)
        {
            case "uint8": return data.ToArray();
            case "int32":
                var ints = new int[data.Length / 4];
                for (var i = 0; i < ints.Length; i++) ints[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]);
                return ints;
            case "int16":
                var signed = new float[data.Length / 2];
                for (var i = 0; i < signed.Length; i++)
                {
                    var value = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]);
                    signed[i] = value == short.MinValue ? float.NaN : (float)(value * entry.Scale);
                }
                return signed;
            case "uint16":
                var unsigned = new float[data.Length / 2];
                for (var i = 0; i < unsigned.Length; i++)
                {
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
                    unsigned[i] = value == ushort.MaxValue ? float.NaN : (float)(value * entry.Scale);
                }
                return unsigned;
            default:
                throw new DroughtDataUnavailableException($"The climate trend array {entry.Name} has unsupported type {entry.Dtype}.");
        }
    }
}
