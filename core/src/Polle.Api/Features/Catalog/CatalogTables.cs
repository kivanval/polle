namespace Polle.Api.Features.Catalog;

/// <summary>
/// The solver reference tables experts may edit (the data team's tables in <c>public</c>). Only these names are ever put
/// into SQL; columns, keys and links are read from the database itself (see <see cref="CatalogSchemaReader"/>).
/// </summary>
/// <param name="Label">SQL for a human label of a row, over alias <c>t</c>; used where other tables link to this one.</param>
/// <param name="Translations">jsonb columns holding names per language, like {"en": "Corn", "uk": "…"}.</param>
public sealed record CatalogTable(string Name, string Title, string Group, string Description, string Label, string[] Translations);

public static class CatalogTables
{
    private static readonly CatalogTable[] All =
    [
        new("crops", "Crops", "Crops", "Crop species the planner knows. Names are kept per language.",
            "coalesce(t.name->>'en', t.scientific_name, t.id::text)", ["name"]),
        new("crop_profiles", "Crop profiles", "Crops", "Growing systems of a crop: temperature and rain limits, season length, yield and the product it gives.",
            "t.system_name", []),
        new("crop_profile_stages", "Profile stages", "Crops", "Ordered stages of a crop profile and the accumulated growing degree days that end each one.",
            "(SELECT p.system_name FROM public.crop_profiles p WHERE p.id = t.crop_profile_id) || ' · ' || coalesce(t.name, 'stage ' || t.sequence_order)", []),
        new("crop_profile_costs", "Profile costs", "Crops", "Planting material cost per hectare of a crop profile, by year.", "t.year::text", []),
        new("crop_rotation_rules", "Rotation rules", "Crops", "How many months must pass after a predecessor crop before a crop can follow it.", "t.id::text", []),
        new("stage_alternatives", "Stage alternatives", "Operations", "Ways to carry out a stage. Each needs machinery and labour hours per hectare.",
            "coalesce(t.description, 'alternative ' || t.id)", []),
        new("alternative_equipment", "Alternative machinery", "Operations", "Machine hours per hectare an alternative needs, by machinery type.", "t.alternative_id::text", []),
        new("alternative_labor", "Alternative labour", "Operations", "Labour hours per hectare an alternative needs, by labour type.", "t.alternative_id::text", []),
        new("equipment_categories", "Machinery types", "Machinery and labour", "Machinery types. Farms record their machinery against these, so deleting one is blocked while a farm uses it.",
            "t.name", []),
        new("equipment_economics", "Machinery costs", "Machinery and labour", "Operating cost and operator rate per hour of each machinery type, by year.", "t.year::text", []),
        new("labor_categories", "Labour types", "Machinery and labour", "Kinds of farm labour.", "t.name", []),
        new("labor_economics", "Labour costs", "Machinery and labour", "Hourly rate of each labour type, by year.", "t.year::text", []),
        new("products", "Products", "Market", "What crops are sold as, and the unit they are priced in.", "coalesce(t.name->>'en', t.id::text)", ["name"]),
        new("product_market_prices", "Market prices", "Market", "Price per unit of each product, by year.", "t.year::text", []),
    ];

    public static IReadOnlyList<CatalogTable> List => All;
    public static CatalogTable? Find(string name) => All.FirstOrDefault(table => table.Name == name);
}
