using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Features.Agriculture;

/// <summary>
/// The fertilizer products felder knows (public.fertilizers), named from public.materials. Read column by column, not
/// through EF's raw-SQL mapping, so the list doesn't hinge on column names matching EF's naming convention, and a
/// product with a missing name, material row or nutrient figure is still listed instead of failing the whole catalog.
/// </summary>
public static class FertilizerCatalog
{
    /// <summary>One catalog row as stored; any figure but the id may be missing.</summary>
    internal sealed record Row(long MaterialId, string? Name, double? NPercent, double? PPercent, double? KPercent);

    // name is jsonb ({"en": …, "uk": …}): the English name, like the rest of the catalog.
    private const string Query = """
        SELECT f.material_id::bigint, coalesce(m.name->>'en', m.name #>> '{}'), f.n_percent::float8, f.p_percent::float8, f.k_percent::float8
        FROM public.fertilizers f LEFT JOIN public.materials m ON m.id = f.material_id
        """;

    /// <param name="ids">Only these products; null for the whole catalog.</param>
    public static async Task<FertilizerResponse[]> ListAsync(AppDbContext db, long[]? ids, CancellationToken ct)
    {
        if (ids is { Length: 0 }) return [];
        var rows = new List<Row>();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(ids is null ? Query : Query + " WHERE f.material_id = ANY($1)",
                (NpgsqlConnection)db.Database.GetDbConnection());
            if (ids is not null) command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = ids });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new Row(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    Nullable(reader, 2), Nullable(reader, 3), Nullable(reader, 4)));
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
        return ToResponses(rows);
    }

    /// <summary>
    /// Sorted by name for the picker. A product without a name shows its id, and a missing or impossible nutrient figure
    /// counts as none of that nutrient.
    /// </summary>
    internal static FertilizerResponse[] ToResponses(IEnumerable<Row> rows) => rows
        .GroupBy(row => row.MaterialId).Select(group => group.First())
        .Select(row => new FertilizerResponse(row.MaterialId,
            string.IsNullOrWhiteSpace(row.Name) ? $"Fertilizer {row.MaterialId}" : row.Name.Trim(),
            Percent(row.NPercent), Percent(row.PPercent), Percent(row.KPercent)))
        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id)
        .ToArray();

    private static double Percent(double? value) => value is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100 ? percent : 0;

    private static double? Nullable(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);
}
