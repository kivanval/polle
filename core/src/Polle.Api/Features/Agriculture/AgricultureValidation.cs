using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace Polle.Api.Features.Agriculture;

internal static class AgricultureValidation
{
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    public static Dictionary<string, string[]> Farm(FarmRequest request)
    {
        var errors = NameAndDescription(request.Name, request.Description);
        MaxLength(errors, "location", request.Location, 500);
        return errors;
    }

    public static Point? HomePoint(PointGeometry? point, Dictionary<string, string[]> errors)
    {
        if (point?.Type != "Point" || point.Coordinates is not [var longitude, var latitude]
            || !double.IsFinite(longitude) || !double.IsFinite(latitude)
            || longitude is < -180 or > 180 || latitude is <= -90 or >= 90)
        {
            errors["homePoint"] = ["Place the farm on the map: a GeoJSON Point with [longitude, latitude] within [-180, 180] and (-90, 90)."];
            return null;
        }
        return Wgs84.CreatePoint(new Coordinate(longitude, latitude));
    }

    public static Dictionary<string, string[]> NameAndDescription(string? name, string? description)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) { errors["name"] = ["Name is required."]; }
        MaxLength(errors, "name", name, 150);
        MaxLength(errors, "description", description, 2000);
        return errors;
    }

    public static Dictionary<string, string[]> Planting(PlantingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.CropProfileId is not > 0) { errors["cropProfileId"] = ["Choose a crop and how it is grown."]; }
        MaxLength(errors, "notes", request.Notes, 4000);
        if (request.SeasonYear is < 1 or > 9999) { errors["seasonYear"] = ["Season year must be between 1 and 9999."]; }
        if (request.PlantingDate is null) { errors["plantingDate"] = ["Planting date is required to establish the growing period."]; }
        // Npgsql reserves DateOnly.MinValue/MaxValue for PostgreSQL infinity by default.
        if (request.PlantingDate is { } start && (start == DateOnly.MinValue || start == DateOnly.MaxValue))
        {
            errors["plantingDate"] = ["Use a date between 0001-01-02 and 9999-12-30; the outer dates are reserved by the database driver."];
        }
        if (request.HarvestingDate is { } end && (end == DateOnly.MinValue || end == DateOnly.MaxValue))
        {
            errors["harvestingDate"] = ["Use a date between 0001-01-02 and 9999-12-30, or null for an open growing period."];
        }
        if (request.IrrigationMm is { } water && (double.IsNaN(water) || water < 0 || water > 5000))
        {
            errors["irrigationMm"] = ["Irrigation over the season must be between 0 and 5000 mm."];
        }
        if (request.FertilizerMaterialId is not null && request.FertilizerRateKgHa is not { } rate)
        {
            errors["fertilizerRateKgHa"] = ["Add how many kilograms of the fertilizer go on each hectare."];
        }
        if (request.FertilizerRateKgHa is { } kg && (double.IsNaN(kg) || kg <= 0 || kg > 10000))
        {
            errors["fertilizerRateKgHa"] = ["Fertilizer per hectare must be above 0 and at most 10000 kg."];
        }
        if (request.FertilizerMaterialId is null && request.FertilizerRateKgHa is not null)
        {
            errors["fertilizerMaterialId"] = ["Choose the fertilizer product."];
        }
        if (request.IrrigationTargetPercent is { } refill && (double.IsNaN(refill) || refill < 0 || refill > 100))
        {
            errors["irrigationTargetPercent"] = ["The irrigation target must be between 0 and 100%."];
        }
        if (request.FertilizerTargetPercent is { } cover && (double.IsNaN(cover) || cover < 0 || cover > 100))
        {
            errors["fertilizerTargetPercent"] = ["The fertilizer target must be between 0 and 100%."];
        }
        if (request.PlantingDate is { } planted && request.HarvestingDate is { } harvested && harvested < planted)
        {
            errors["harvestingDate"] = ["Harvesting date cannot precede planting date."];
        }
        return errors;
    }

    public static Polygon? Boundary(PolygonGeometry? boundary, Dictionary<string, string[]> errors)
    {
        Polygon? Invalid(string message) { errors["boundary"] = [message]; return null; }
        if (boundary?.Type != "Polygon" || boundary.Coordinates is not { Length: 1 })
        {
            return Invalid("Use a GeoJSON Polygon with exactly one exterior ring. Holes and MultiPolygon are not supported.");
        }
        var ring = boundary.Coordinates[0];
        if (ring is null || ring.Length is < 4 or > 501)
        {
            return Invalid("The ring must contain 3–500 vertices plus a closing copy of the first position.");
        }
        if (ring.Any(point => point is null || point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1])
            || point[0] < -180 || point[0] > 180 || point[1] <= -90 || point[1] >= 90))
        {
            return Invalid("Positions must be finite [longitude, latitude] pairs within [-180, 180] and (-90, 90); poles are unsupported.");
        }
        if (!ring[0].SequenceEqual(ring[^1])) { return Invalid("The ring must be explicitly closed (last position equals first)."); }
        var vertices = ring[..^1].Select(point => (point[0], point[1])).ToArray();
        if (vertices.Distinct().Count() != vertices.Length) { return Invalid("Vertices must be distinct, except the closing position."); }
        if (ring.Max(point => point[0]) - ring.Min(point => point[0]) >= 180)
        {
            return Invalid("Polygons crossing the antimeridian or spanning 180 degrees are unsupported.");
        }
        var polygon = Wgs84
            .CreatePolygon(ring.Select(point => new Coordinate(point[0], point[1])).ToArray());
        if (!new IsValidOp(polygon).IsValid || polygon.IsEmpty || polygon.Area <= 0)
        {
            return Invalid("The boundary must enclose an area without self-intersections or degenerate edges.");
        }
        // Area above only detects a degenerate ring. Hectares are computed by PostGIS on the spheroid.
        return Orientation.IsCCW(polygon.ExteriorRing.Coordinates) ? polygon : (Polygon)polygon.Reverse();
    }

    public static Dictionary<string, string[]> FleetItem(FleetItemRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EquipmentCategoryId is null) { errors["equipmentCategoryId"] = ["Choose a machinery type."]; }
        if (request.UnitsCount is { } units && units is < 1 or > 1000) { errors["unitsCount"] = ["If supplied, the number of units must be from 1 to 1000."]; }
        if (request.MonthlyHoursPerUnit is { } months
            && (months.Length != 12 || months.Any(hours => hours < 0 || hours > 744 || decimal.Round(hours, 1) != hours)))
        {
            errors["monthlyHoursPerUnit"] = ["Give 12 monthly limits, January first, each from 0 to 744 hours with one decimal at most, or null if availability is unspecified."];
        }
        MaxLength(errors, "customName", request.CustomName, 100);
        return errors;
    }

    public static Dictionary<string, string[]> Budget(BudgetRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.BudgetUsd is { } budget && (budget < 0 || budget >= 1_000_000_000_000m || decimal.Round(budget, 2) != budget))
        {
            errors["budgetUsd"] = ["Budget must be 0 or more, below 1 trillion USD, with cents at most."];
        }
        return errors;
    }

    public static Dictionary<string, string[]> Allocation(BudgetAllocationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.AmountUsd is { } amount && (amount < 0 || amount >= 1_000_000_000_000m || decimal.Round(amount, 2) != amount))
        {
            errors["amountUsd"] = ["Amount must be 0 or more, below 1 trillion USD, with cents at most."];
        }
        return errors;
    }

    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The farmer's drought level, drainage and texture: each blank (unknown) or one of its listed classes.</summary>
    public static void FieldConditions(FieldRequest request, Dictionary<string, string[]> errors)
    {
        OneOf(errors, "droughtLevel", request.DroughtLevel, Domain.Agriculture.FieldConditions.DroughtLevels, "Choose drought_prone, moderate or low, or leave it empty.");
        OneOf(errors, "soilDrainage", request.SoilDrainage, Domain.Agriculture.FieldConditions.Drainages, "Choose well_drained, moderate or poor, or leave it empty.");
        OneOf(errors, "soilTexture", request.SoilTexture, Domain.Agriculture.FieldConditions.Textures,
            "Choose sand, loamy_sand, sandy_loam, loam, silt_loam, clay_loam, clay or peat, or leave it empty.");
        const double nutrientMax = Domain.Agriculture.FieldConditions.MaxInitialNutrientKg;
        Between(errors, "initialNKg", request.InitialNKg, nutrientMax, "Starting nitrogen must be 0 to 2000 kg/ha, or empty.");
        Between(errors, "initialPKg", request.InitialPKg, nutrientMax, "Starting phosphorus must be 0 to 2000 kg/ha, or empty.");
        Between(errors, "initialKKg", request.InitialKKg, nutrientMax, "Starting potassium must be 0 to 2000 kg/ha, or empty.");
        Between(errors, "initialDepletionMm", request.InitialDepletionMm, Domain.Agriculture.FieldConditions.MaxInitialDepletionMm,
            "Starting water deficit must be 0 to 1000 mm, or empty.");
    }

    /// <summary>An optional measurement: blank, or a finite number from 0 to <paramref name="maximum"/>.</summary>
    private static void Between(Dictionary<string, string[]> errors, string key, double? value, double maximum, string message)
    {
        if (value is { } given && !(double.IsFinite(given) && given >= 0 && given <= maximum)) errors[key] = [message];
    }

    private static void OneOf(Dictionary<string, string[]> errors, string key, string? value, IReadOnlyList<string> allowed, string message)
    {
        if (Optional(value) is { } given && !allowed.Contains(given)) errors[key] = [message];
    }

    private static void MaxLength(Dictionary<string, string[]> errors, string key, string? value, int maximum)
    {
        if (value?.Length > maximum) { errors[key] = [$"Maximum length is {maximum} characters."]; }
    }
}
