using System.Globalization;
using System.Security.Claims;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Assistant;

public sealed record AssistantFarm(Guid Id, string Name, string? Location, decimal? BudgetUsd, string Currency);
public sealed record AssistantField(Guid Id, Guid FarmId, string FarmName, string Name, double AreaHectares);
public sealed record AssistantFarmSummary(AssistantFarm Farm, AssistantField[] Fields);
public sealed record AssistantPlanting(string CropId, string CropName, int SeasonYear, DateOnly PlantingDate, DateOnly? HarvestingDate);
public sealed record AssistantFieldHistory(AssistantField Field, AssistantPlanting[] Plantings, int? Year);
public sealed record ToolResolution<T>(string Status, T? Value, string[] Choices);

public sealed class FarmAssistantTools(AppDbContext db)
{
    public async Task<AssistantFarm[]> ListMyFarmsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = UserId(user);
        return await db.Farms.AsNoTracking().Where(farm => farm.OwnerUserId == userId)
            .OrderBy(farm => farm.CreatedAtUtc).ThenBy(farm => farm.Id)
            .Select(farm => new AssistantFarm(farm.Id, farm.Name, farm.Location, farm.BudgetUsd, "USD"))
            .ToArrayAsync(ct);
    }

    public async Task<ToolResolution<AssistantFarmSummary>> GetFarmSummaryAsync(ClaimsPrincipal user,
        Guid? farmId, string? farmName, Guid? contextFarmId, CancellationToken ct, string? requestText = null)
    {
        var farms = await ListMyFarmsAsync(user, ct);
        AssistantFarm[] matches;
        if (farmId is { } id)
            matches = farms.Where(farm => farm.Id == id).ToArray();
        else
        {
            var reference = farmName?.Trim().Trim('"', '\'', '«', '»', '.', '?', '!', ':', ' ');
            var exact = string.IsNullOrEmpty(reference) ? [] : farms.Where(farm => string.Equals(farm.Name.Trim(), reference,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var namedInRequest = string.IsNullOrWhiteSpace(requestText) ? [] : NamedInText(requestText, farms);
            if (namedInRequest.Length > 1 && exact.Length == 1 && namedInRequest.Contains(exact[0]))
                matches = exact;
            else if (namedInRequest.Length > 0)
                matches = namedInRequest;
            else if (!string.IsNullOrEmpty(reference))
                matches = exact;
            else
                matches = contextFarmId is { } currentId ? farms.Where(farm => farm.Id == currentId).ToArray() : farms;
        }

        if (matches.Length == 0) return new("not_found", null, []);
        if (matches.Length > 1) return new("ambiguous", null, matches.Select(farm => $"{farm.Name} ({farm.Id})").ToArray());

        var selected = matches[0];
        var userId = UserId(user);
        var fields = await db.Fields.AsNoTracking()
            .Where(field => field.FarmId == selected.Id && field.Farm.OwnerUserId == userId)
            .OrderBy(field => field.Name).ThenBy(field => field.Id)
            .Select(field => new AssistantField(field.Id, field.FarmId, selected.Name, field.Name, field.AreaHectares))
            .ToArrayAsync(ct);
        return new("found", new AssistantFarmSummary(selected, fields), []);
    }

    // Farms whose full name appears as a whole phrase. A mention that lies entirely inside a longer matched
    // name ("Home" within "Home Romny") is not a separate reference; equal names stay ambiguous.
    private static AssistantFarm[] NamedInText(string text, AssistantFarm[] farms)
    {
        var mentions = farms.Select(farm => (Farm: farm, Spans: NameSpans(text, farm.Name.Trim())))
            .Where(item => item.Spans.Count > 0).ToArray();
        return mentions.Where(item => item.Spans.Any(span => !mentions.Any(other =>
                other.Farm.Name.Trim().Length > item.Farm.Name.Trim().Length &&
                other.Spans.Any(outer => outer.Start <= span.Start && span.End <= outer.End))))
            .Select(item => item.Farm).ToArray();
    }

    private static List<(int Start, int End)> NameSpans(string text, string name)
    {
        var spans = new List<(int Start, int End)>();
        if (string.IsNullOrWhiteSpace(name)) return spans;
        var start = 0;
        while ((start = text.IndexOf(name, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + name.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) &&
                (end == text.Length || !char.IsLetterOrDigit(text[end]))) spans.Add((start, end));
            start++;
        }
        return spans;
    }

    public async Task<ToolResolution<AssistantFieldHistory>> GetFieldPlantingHistoryAsync(ClaimsPrincipal user,
        Guid? fieldId, string? fieldName, Guid? contextFarmId, Guid? contextFieldId, int? year, CancellationToken ct)
    {
        if (year is < 1900 or > 2100) return new("invalid_year", null, []);
        var userId = UserId(user);
        var ownedFields = await db.Fields.AsNoTracking().Where(field => field.Farm.OwnerUserId == userId)
            .OrderBy(field => field.Name).ThenBy(field => field.Id)
            .Select(field => new AssistantField(field.Id, field.FarmId, field.Farm.Name, field.Name, field.AreaHectares))
            .ToArrayAsync(ct);

        AssistantField[] matches;
        if (fieldId is { } id)
            matches = ownedFields.Where(field => field.Id == id).ToArray();
        else if (!string.IsNullOrWhiteSpace(fieldName))
            matches = ownedFields.Where(field => string.Equals(field.Name, fieldName.Trim(), StringComparison.OrdinalIgnoreCase)
                && (contextFarmId is null || field.FarmId == contextFarmId)).ToArray();
        else if (contextFieldId is { } currentId)
            matches = ownedFields.Where(field => field.Id == currentId &&
                (contextFarmId is null || field.FarmId == contextFarmId)).ToArray();
        else
            matches = ownedFields.Where(field => contextFarmId is null || field.FarmId == contextFarmId).ToArray();

        if (matches.Length == 0) return new("not_found", null, []);
        if (matches.Length > 1) return new("ambiguous", null, matches.Select(field => $"{field.Name} ({field.FarmName})").ToArray());

        var selected = matches[0];
        var records = await db.FieldPlantings.AsNoTracking()
            .Where(planting => planting.FieldId == selected.Id && planting.Field.Farm.OwnerUserId == userId)
            .OrderBy(planting => planting.PlantingDate).ThenBy(planting => planting.Id)
            .Select(planting => new { CropName = planting.CropProfile.Crop.NameUk ?? planting.CropProfile.Crop.Name,
                planting.CropProfileId, planting.SeasonYear, planting.PlantingDate, planting.HarvestingDate })
            .ToArrayAsync(ct);
        if (year is { } targetYear) records = records.Where(record => record.SeasonYear == targetYear).ToArray();
        var plantings = records.Select(record => new AssistantPlanting(record.CropProfileId.ToString(CultureInfo.InvariantCulture),
            record.CropName, record.SeasonYear, record.PlantingDate, record.HarvestingDate)).ToArray();
        return new("found", new AssistantFieldHistory(selected, plantings, year), []);
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : throw new UnauthorizedAccessException("A valid session is required.");
}
