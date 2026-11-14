namespace Polle.Domain.Agriculture;

/// <summary>
/// Soil and climate facts a farmer may record for a field. Drainage and texture use the solver's (felder) soil classes,
/// so they pass to it unchanged.
/// </summary>
public static class FieldConditions
{
    public const string DroughtProne = "drought_prone";
    public const string DroughtModerate = "moderate";
    public const string DroughtLow = "low";

    public static IReadOnlyList<string> DroughtLevels { get; } = [DroughtProne, DroughtModerate, DroughtLow];
    public static IReadOnlyList<string> Drainages { get; } = ["well_drained", "moderate", "poor"];
    public static IReadOnlyList<string> Textures { get; } = ["sand", "loamy_sand", "sandy_loam", "loam", "silt_loam", "clay_loam", "clay", "peat"];

    /// <summary>Highest starting N, P or K a farmer may enter, kg/ha: well above any arable soil test.</summary>
    public const double MaxInitialNutrientKg = 2000;
    /// <summary>Highest starting root-zone water deficit a farmer may enter, mm: more than any soil can hold.</summary>
    public const double MaxInitialDepletionMm = 1000;
}
