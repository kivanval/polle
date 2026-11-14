using System.Text.Json;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Polle.Api.Features.Fields;

/// <summary>
/// Tells land from sea with the Natural Earth 1:50m land polygons (public domain, embedded as Data/land-50m.json).
/// The coastline there is generalised by about 1-2 km, so callers test several points of a field, not one.
/// </summary>
public sealed class LandMask
{
    private const string Resource = "Polle.Api.land-50m.json";
    private static readonly Lazy<LandMask> Embedded = new(Load);

    private readonly STRtree<(Envelope Envelope, IndexedPointInAreaLocator Locator)> index = new();

    public static LandMask Default => Embedded.Value;

    private LandMask(IEnumerable<Polygon> polygons)
    {
        foreach (var polygon in polygons)
            index.Insert(polygon.EnvelopeInternal, (polygon.EnvelopeInternal, new IndexedPointInAreaLocator(polygon)));
        index.Build();
    }

    /// <summary>Whether the WGS84 point (longitude, latitude) is on land; a point on the coastline itself counts as land.</summary>
    public bool Contains(double longitude, double latitude)
    {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude)) return false;
        var coordinate = new Coordinate(longitude, latitude);
        foreach (var (envelope, locator) in index.Query(new Envelope(coordinate)))
            if (envelope.Contains(coordinate) && locator.Locate(coordinate) != Location.Exterior) return true;
        return false;
    }

    private static LandMask Load()
    {
        using var stream = typeof(LandMask).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The land mask resource {Resource} is missing from the build.");
        using var json = JsonDocument.Parse(stream);
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var polygons = new List<Polygon>();
        foreach (var polygon in json.RootElement.GetProperty("polygons").EnumerateArray())
        {
            var rings = polygon.EnumerateArray().Select(ring => factory.CreateLinearRing(Coordinates(ring))).ToArray();
            polygons.Add(factory.CreatePolygon(rings[0], rings[1..]));
        }
        return new LandMask(polygons);
    }

    private static Coordinate[] Coordinates(JsonElement flat)
    {
        var values = flat.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var coordinates = new Coordinate[values.Length / 2];
        for (var index = 0; index < coordinates.Length; index++) coordinates[index] = new Coordinate(values[2 * index], values[2 * index + 1]);
        // Rounding can open a ring by a hair; a linear ring must end where it starts.
        return coordinates[0].Equals2D(coordinates[^1]) ? coordinates : [.. coordinates, coordinates[0]];
    }
}
