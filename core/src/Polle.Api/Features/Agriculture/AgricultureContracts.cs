using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Polle.Api.Features.Fields;
using Polle.Domain.Agriculture;
using NetTopologySuite.Geometries;

namespace Polle.Api.Features.Agriculture;

public sealed record FarmRequest(string? Name, string? Description = null, string? Location = null, PointGeometry? HomePoint = null);
public sealed record FarmResponse(Guid Id, string Name, string? Description, string? Location, PointGeometry? HomePoint,
    [property: Description("Total farm budget in USD; null until set. Changed only through PUT /api/farms/{farmId}/budget.")] decimal? BudgetUsd,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static FarmResponse From(Farm farm) => new(farm.Id, farm.Name, farm.Description, farm.Location,
        farm.HomePoint is null ? null : PointGeometry.From(farm.HomePoint), farm.BudgetUsd, farm.CreatedAtUtc, farm.UpdatedAtUtc);
}

public sealed record FarmNameRequest([property: Required, MaxLength(150)] string? Name);

public sealed record BudgetRequest([property: Description("USD, 0 or more with at most 2 decimals; null clears the budget.")] decimal? BudgetUsd);

public sealed record BudgetAllocationRequest(
    [property: Description("USD, 0 or more with at most 2 decimals; null or 0 removes the category.")] decimal? AmountUsd);
public sealed record BudgetAllocationItem(string Category, decimal AmountUsd, DateTime UpdatedAtUtc);
/// <summary>Planning breakdown of a farm budget. BudgetUsd stays the solver's total; categories never replace it.</summary>
public sealed record BudgetAllocationsResponse(Guid FarmId, decimal? BudgetUsd,
    [property: Description("Category keys in display order: seeds, fertilizers, crop_protection, fuel, machinery, labor, irrigation, other.")] string[] Categories,
    BudgetAllocationItem[] Allocations, decimal AllocatedUsd,
    [property: Description("BudgetUsd − AllocatedUsd; negative when over-allocated, null without a budget.")] decimal? UnallocatedUsd)
{
    public static BudgetAllocationsResponse From(Guid farmId, decimal? budget, IEnumerable<FarmBudgetAllocation> allocations)
    {
        var items = allocations.OrderBy(item => Array.IndexOf(FarmBudgetAllocation.Categories, item.Category))
            .Select(item => new BudgetAllocationItem(item.Category, item.AmountUsd, item.UpdatedAtUtc)).ToArray();
        var allocated = items.Sum(item => item.AmountUsd);
        return new(farmId, budget, FarmBudgetAllocation.Categories, items, allocated, budget - allocated);
    }
}

public sealed record LaborCategoryResponse(int Id, string Name);

/// <summary>One farm in the cross-farm resource view. Area counts top-level fields only, since subfields lie inside them.</summary>
public sealed record FarmResourceSummary(Guid Id, string Name, string? Location, decimal? BudgetUsd, BudgetAllocationItem[] Allocations,
    decimal AllocatedUsd, decimal? UnallocatedUsd, int FieldCount, double AreaHectares, FleetItemResponse[] Fleet);
public sealed record ResourceOverviewResponse(FarmResourceSummary[] Farms, string[] BudgetCategories);

public sealed record EquipmentCategoryResponse(int Id, string Slug, string Name, string? Description)
{
    public static EquipmentCategoryResponse From(EquipmentCategory category) => new(category.Id, category.Slug, category.Name, category.Description);
}

/// <summary>One machinery category/model on a farm; quantity and historical monthly limits may be unknown.</summary>
public sealed record FleetItemRequest(
    [property: Required, Description("Id from GET /api/equipment-categories.")] int? EquipmentCategoryId,
    [property: Range(1, 1000), Description("Optional known number of units; null means unknown, not one.")] int? UnitsCount = null,
    [property: Description("Hour limit for one unit in each month, January first: exactly 12 values from 0 to 744 with one decimal. Null = availability unspecified; no solver fallback is implied.")] decimal[]? MonthlyHoursPerUnit = null,
    [property: MaxLength(100), Description("Optional make/model, e.g. John Deere 8R 410.")] string? CustomName = null);
public sealed record FleetDetailsRequest(
    [property: Required, Description("Id from GET /api/equipment-categories.")] int? EquipmentCategoryId,
    [property: MaxLength(100)] string? CustomName = null);
public sealed record FleetItemResponse(int Id, Guid FarmId, int EquipmentCategoryId, string? CustomName, int? UnitsCount, decimal[]? MonthlyHoursPerUnit,
    [property: Description("Sum of MonthlyHoursPerUnit; null without limits.")] decimal? AnnualHoursPerUnit,
    [property: Description("UnitsCount × AnnualHoursPerUnit; null when quantity or limits are unknown.")] decimal? AnnualHoursTotal,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static FleetItemResponse From(FarmEquipment item)
    {
        var annual = item.MonthlyHoursPerUnit?.Sum();
        return new(item.Id, item.FarmId, item.EquipmentCategoryId, item.CustomName, item.UnitsCount, item.MonthlyHoursPerUnit,
            annual, annual * item.UnitsCount, item.CreatedAtUtc, item.UpdatedAtUtc);
    }
}

/// <summary>WGS84 GeoJSON Point: [longitude, latitude].</summary>
public sealed record PointGeometry(string? Type, double[]? Coordinates)
{
    public static PointGeometry From(Point point) => new("Point", [point.X, point.Y]);
}

/// <summary>WGS84 GeoJSON Polygon. One closed exterior ring of [longitude, latitude] positions.</summary>
public sealed record PolygonGeometry(string? Type, double[][][]? Coordinates)
{
    public static PolygonGeometry From(Polygon polygon) => new("Polygon",
        [polygon.ExteriorRing.Coordinates.Select(point => new[] { point.X, point.Y }).ToArray()]);
}

public sealed record FieldRequest(string? Name, PolygonGeometry? Boundary, string? Description = null,
    [property: Description("Create only: a top-level field of the same farm that this subfield divides. Its boundary must lie inside the parent. Ignored on update.")] Guid? ParentFieldId = null,
    [property: Description("How dry the field runs: drought_prone, moderate or low; null when unknown. Replaced on update like the other details.")] string? DroughtLevel = null,
    [property: Description("Soil drainage: well_drained, moderate or poor; null keeps the estimated soil's.")] string? SoilDrainage = null,
    [property: Description("Soil texture: sand, loamy_sand, sandy_loam, loam, silt_loam, clay_loam, clay or peat; null keeps the estimated soil's.")] string? SoilTexture = null,
    [property: Description("Plant-available nitrogen when the plan starts, kg/ha, 0-2000 (a soil test); null keeps the estimate. The solver gets it as soil.nutrients.nitrogen.initial_kg_ha.")] double? InitialNKg = null,
    [property: Description("Plant-available phosphorus when the plan starts, kg/ha, 0-2000; null keeps the estimate.")] double? InitialPKg = null,
    [property: Description("Plant-available potassium when the plan starts, kg/ha, 0-2000; null keeps the estimate.")] double? InitialKKg = null,
    [property: Description("Water missing from the root zone when the plan starts, mm below field capacity (0 = fully wet), 0-1000; null derives it from the drought level.")] double? InitialDepletionMm = null);
public sealed record FieldResponse(Guid Id, Guid FarmId,
    [property: Description("Set for a subfield. Its area is part of the parent's, so totals and the solver count top-level fields only.")] Guid? ParentFieldId,
    string Name, string? Description, double AreaHectares, PolygonGeometry Boundary, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    [property: Description("False when the field has no soil data (it lies in the sea), so the solver cannot model it and strategy runs are refused with 422.")] bool SoilAvailable,
    string? DroughtLevel = null, string? SoilDrainage = null, string? SoilTexture = null,
    double? InitialNKg = null, double? InitialPKg = null, double? InitialKKg = null, double? InitialDepletionMm = null)
{
    public static FieldResponse From(Field field) => new(field.Id, field.FarmId, field.ParentFieldId, field.Name, field.Description,
        field.AreaHectares, PolygonGeometry.From(field.Boundary), field.CreatedAtUtc, field.UpdatedAtUtc,
        FieldSoil.Available(field.Boundary, LandMask.Default), field.DroughtLevel, field.SoilDrainage, field.SoilTexture,
        field.InitialNKg, field.InitialPKg, field.InitialKKg, field.InitialDepletionMm);
}

public sealed record CropProfileResponse(long Id, string SystemName, bool IsPerennial);

public sealed record CropResponse(long Id, string Name, string? NameUk, string? ScientificName,
    [property: Description("Ways to grow this crop, ordered by id. A planting names one of them.")] CropProfileResponse[] Profiles)
{
    public static CropResponse From(Crop crop) => new(crop.Id, crop.Name, crop.NameUk, crop.ScientificName,
        crop.Profiles.OrderBy(profile => profile.Id).Select(profile => new CropProfileResponse(profile.Id, profile.SystemName, profile.IsPerennial)).ToArray());
}

/// <summary>One growing period. Dates are inclusive; a null harvestingDate means the period remains open.</summary>
public sealed record PlantingRequest(
    [property: Required, Description("Crop profile id from GET /api/crops (crops[].profiles[].id).")] long? CropProfileId,
    [property: Range(1, 9999), Description("Grouping label; multiple sequential plantings in the same year are allowed.")] int SeasonYear,
    [property: Required, Description("Inclusive start, YYYY-MM-DD. Required to establish a non-overlapping growing period.")] DateOnly? PlantingDate,
    [property: Description("Inclusive final growing day; null reserves the field indefinitely.")] DateOnly? HarvestingDate = null,
    [property: MaxLength(4000)] string? Notes = null,
    [property: Description("Irrigation planned over the whole season, mm (0–5000); null or 0 is rain-fed.")] double? IrrigationMm = null,
    [property: Description("Fertilizer product from GET /api/fertilizers; null for none. Needs fertilizerRateKgHa.")] long? FertilizerMaterialId = null,
    [property: Description("Kilograms of the fertilizer product per hectare over the season (above 0, up to 10000).")] double? FertilizerRateKgHa = null);
public sealed record PlantingResponse(Guid Id, Guid FieldId, long CropProfileId, long CropId, int SeasonYear,
    DateOnly PlantingDate, DateOnly? HarvestingDate, string? Notes, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    [property: Description("Derived from today's UTC date: planned, current or historical. Not a stored flag.")] string Status,
    double? IrrigationMm = null, long? FertilizerMaterialId = null, double? FertilizerRateKgHa = null)
{
    public static PlantingResponse From(FieldPlanting planting, DateOnly today) => new(planting.Id, planting.FieldId, planting.CropProfileId, planting.CropProfile.CropId,
        planting.SeasonYear, planting.PlantingDate, planting.HarvestingDate, planting.Notes, planting.CreatedAtUtc, planting.UpdatedAtUtc,
        planting.PlantingDate > today ? "planned" : planting.HarvestingDate is { } end && end < today ? "historical" : "current",
        planting.IrrigationMm, planting.FertilizerMaterialId, planting.FertilizerRateKgHa);
}

/// <summary>A fertilizer product a planting can name, with its N, P₂O₅ and K₂O content as on the label, percent.</summary>
public sealed record FertilizerResponse(long Id, string Name, double NitrogenPercent, double PhosphatePercent, double PotashPercent);
