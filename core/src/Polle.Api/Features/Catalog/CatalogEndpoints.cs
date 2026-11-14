using System.Text.Json;
using System.Text.Json.Nodes;
using Polle.Api.Common;
using Polle.Api.Features.Agriculture;
using Polle.Domain.Catalog;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Polle.Api.Features.Catalog;

public sealed record CatalogTableSummary(string Name, string Title, string Group, string Description, long Rows);
public sealed record CatalogReference(string Table, string[] Columns, string OnDelete);
public sealed record CatalogTableResponse(string Name, string Title, string Group, string Description, string[] Key, CatalogColumn[] Columns,
    CatalogUnique[] Uniques, string[] Checks, CatalogReference[] ReferencedBy);
public sealed record CatalogRow(JsonElement Values, string Label);
public sealed record CatalogRowsResponse(long Total, CatalogRow[] Rows);
public sealed record CatalogOption(JsonElement Key, string Label);
public sealed record CatalogWriteRequest(JsonObject? Values, JsonObject? Key = null);
public sealed record CatalogDeleteRequest(JsonObject? Key, bool Preview = false);
public sealed record CatalogDeleteResponse(bool Deleted, Dictionary<string, long> Cascade);
public sealed record CatalogChangeResponse(long Id, string Table, string Action, JsonElement Key, JsonElement? Before, JsonElement? After,
    JsonElement? Cascade, string? Editor, bool ByYou, DateTime ChangedAtUtc);

/// <summary>
/// The expert catalog page: list, add, edit and delete rows of the solver's reference tables. Open to every signed-in user
/// (there are no roles); every change is recorded in public.catalog_changes with the user and the row before and after.
/// </summary>
public sealed class CatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Expert catalog");
        group.MapGet("/catalog", ListTablesAsync).Produces<CatalogTableSummary[]>().WithSummary("List editable reference tables with row counts");
        group.MapGet("/catalog/changes", ListChangesAsync).Produces<CatalogChangeResponse[]>().WithSummary("Latest catalog edits, newest first");
        group.MapGet("/catalog/{table}", GetTableAsync).Produces<CatalogTableResponse>().WithSummary("Columns, key, links and rules of one table");
        group.MapGet("/catalog/{table}/rows", ListRowsAsync).Produces<CatalogRowsResponse>().WithSummary("Rows of one table, searchable and paged");
        group.MapGet("/catalog/{table}/options", ListOptionsAsync).Produces<CatalogOption[]>().WithSummary("Key and label of every row, for link pickers");
        group.MapPost("/catalog/{table}/rows", InsertAsync).Produces<CatalogRow>(201).WithSummary("Add a row");
        group.MapPut("/catalog/{table}/rows", UpdateAsync).Produces<CatalogRow>().WithSummary("Change a row identified by its key");
        group.MapPost("/catalog/{table}/rows/delete", DeleteAsync).Produces<CatalogDeleteResponse>()
            .WithSummary("Delete a row, or with preview=true report what cascading foreign keys would also remove");
    }

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static string From(CatalogSchema schema) => "public." + Q(schema.Table.Name);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static async Task<IResult> ListTablesAsync(AppDbContext db, CatalogSchemaReader reader, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        var schemas = CatalogTables.List.Where(table => database.Tables.ContainsKey(table.Name)).ToArray();
        var counts = new Dictionary<string, long>();
        if (schemas.Length > 0)
        {
            var sql = string.Join(" UNION ALL ", schemas.Select(table => $"SELECT '{table.Name}', count(*) FROM public.{Q(table.Name)}"));
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
                await using var rows = await command.ExecuteReaderAsync(ct);
                while (await rows.ReadAsync(ct)) { counts[rows.GetString(0)] = rows.GetInt64(1); }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        return TypedResults.Ok(schemas.Select(table => new CatalogTableSummary(table.Name, table.Title, table.Group, table.Description, counts[table.Name])));
    }

    private static async Task<IResult> GetTableAsync(string table, AppDbContext db, CatalogSchemaReader reader, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema)) { return TypedResults.NotFound(); }
        var referencedBy = database.ForeignKeys.Where(key => key.RefTable == table)
            .Select(key => new CatalogReference(key.Table, key.Columns, key.OnDelete switch { 'c' => "cascade", 'n' => "set null", 'd' => "set default", _ => "restrict" }))
            .ToArray();
        return TypedResults.Ok(new CatalogTableResponse(schema.Table.Name, schema.Table.Title, schema.Table.Group, schema.Table.Description,
            schema.Key, schema.Columns, schema.Uniques, schema.Checks, referencedBy));
    }

    private static async Task<IResult> ListRowsAsync(string table, string? search, int? offset, int? limit, AppDbContext db, CatalogSchemaReader reader, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema)) { return TypedResults.NotFound(); }
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var filter = "(@search::text IS NULL OR to_jsonb(t)::text ILIKE '%' || @search || '%' OR (" + schema.Table.Label + ") ILIKE '%' || @search || '%')";
        var order = string.Join(", ", schema.Key.Select(column => "t." + Q(column)));
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            long total;
            await using (var count = new NpgsqlCommand($"SELECT count(*) FROM {From(schema)} t WHERE {filter}", connection))
            {
                count.Parameters.Add(new NpgsqlParameter("search", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)term ?? DBNull.Value });
                total = (long)(await count.ExecuteScalarAsync(ct))!;
            }
            var rows = new List<CatalogRow>();
            await using var command = new NpgsqlCommand(
                $"SELECT to_jsonb(t)::text, coalesce(({schema.Table.Label})::text, '') FROM {From(schema)} t WHERE {filter} ORDER BY {order} LIMIT {take} OFFSET {skip}", connection);
            command.Parameters.Add(new NpgsqlParameter("search", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)term ?? DBNull.Value });
            await using var result = await command.ExecuteReaderAsync(ct);
            while (await result.ReadAsync(ct)) { rows.Add(new CatalogRow(Json(result.GetString(0)), result.GetString(1))); }
            return TypedResults.Ok(new CatalogRowsResponse(total, [.. rows]));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<IResult> ListOptionsAsync(string table, AppDbContext db, CatalogSchemaReader reader, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema) || schema.Key.Length != 1) { return TypedResults.NotFound(); }
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(
                $"SELECT to_jsonb(t.{Q(schema.Key[0])})::text, coalesce(({schema.Table.Label})::text, '') AS label FROM {From(schema)} t ORDER BY label LIMIT 5000",
                (NpgsqlConnection)db.Database.GetDbConnection());
            var options = new List<CatalogOption>();
            await using var result = await command.ExecuteReaderAsync(ct);
            while (await result.ReadAsync(ct)) { options.Add(new CatalogOption(Json(result.GetString(0)), result.GetString(1))); }
            return TypedResults.Ok(options);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<IResult> InsertAsync(string table, CatalogWriteRequest request, AppDbContext db, CatalogSchemaReader reader, HttpContext context, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema)) { return TypedResults.NotFound(); }
        var errors = new Dictionary<string, string[]>();
        var values = CatalogValues.Clean(schema, request.Values, insert: true, errors);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var columns = values.Select(entry => entry.Key).ToArray();
        var sql = columns.Length == 0
            ? $"INSERT INTO {From(schema)} AS t DEFAULT VALUES"
            : $"INSERT INTO {From(schema)} AS t ({string.Join(", ", columns.Select(Q))}) SELECT {string.Join(", ", columns.Select(column => "r." + Q(column)))} "
              + $"FROM jsonb_populate_record(NULL::{From(schema)}, @values::jsonb) r";
        return await WriteAsync(db, database, schema, context, "insert", ct, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(sql + $" RETURNING to_jsonb(t)::text, coalesce(({schema.Table.Label})::text, '')", connection, transaction);
            command.Parameters.AddWithValue("values", NpgsqlTypes.NpgsqlDbType.Jsonb, values.ToJsonString());
            await using var result = await command.ExecuteReaderAsync(ct);
            await result.ReadAsync(ct);
            return (null, result.GetString(0), result.GetString(1));
        });
    }

    private static async Task<IResult> UpdateAsync(string table, CatalogWriteRequest request, AppDbContext db, CatalogSchemaReader reader, HttpContext context, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema)) { return TypedResults.NotFound(); }
        var errors = new Dictionary<string, string[]>();
        var key = CatalogValues.Key(schema, request.Key, errors);
        var values = CatalogValues.Clean(schema, request.Values, insert: false, errors);
        if (errors.Count > 0 || key is null) { return TypedResults.ValidationProblem(errors); }
        if (values.Count == 0) { return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["values"] = ["Nothing to change."] }); }
        var match = string.Join(" AND ", schema.Key.Select(column => $"t.{Q(column)} = k.{Q(column)}"));
        var keyRecord = $"jsonb_populate_record(NULL::{From(schema)}, @key::jsonb) k";
        return await WriteAsync(db, database, schema, context, "update", ct, async (connection, transaction) =>
        {
            string? before;
            await using (var select = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM {From(schema)} t, {keyRecord} WHERE {match} FOR UPDATE OF t", connection, transaction))
            {
                select.Parameters.AddWithValue("key", NpgsqlTypes.NpgsqlDbType.Jsonb, key.ToJsonString());
                before = (string?)await select.ExecuteScalarAsync(ct);
            }
            if (before is null) { return (null, null, null); }
            var assignments = string.Join(", ", values.Select(entry => $"{Q(entry.Key)} = r.{Q(entry.Key)}"));
            await using var command = new NpgsqlCommand(
                $"UPDATE {From(schema)} AS t SET {assignments} FROM jsonb_populate_record(NULL::{From(schema)}, @values::jsonb) r, {keyRecord} WHERE {match} "
                + $"RETURNING to_jsonb(t)::text, coalesce(({schema.Table.Label})::text, '')", connection, transaction);
            command.Parameters.AddWithValue("values", NpgsqlTypes.NpgsqlDbType.Jsonb, values.ToJsonString());
            command.Parameters.AddWithValue("key", NpgsqlTypes.NpgsqlDbType.Jsonb, key.ToJsonString());
            await using var result = await command.ExecuteReaderAsync(ct);
            await result.ReadAsync(ct);
            return (before, result.GetString(0), result.GetString(1));
        });
    }

    /// <summary>Runs one change in a transaction, records it in catalog_changes, and turns PostgreSQL rule violations into field errors.</summary>
    private static async Task<IResult> WriteAsync(AppDbContext db, CatalogDatabase database, CatalogSchema schema, HttpContext context, string action, CancellationToken ct,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<(string? Before, string? After, string? Label)>> change)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var (before, after, label) = await change((NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
            if (after is null) { return TypedResults.NotFound(); }
            var row = Json(after);
            // Saving a form without changing anything is not worth a log entry.
            if (before is null || !JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(after)))
            {
                db.CatalogChanges.Add(new CatalogChange { UserId = OwnedResources.UserId(context), TableName = schema.Table.Name, Action = action,
                    RowKey = KeyOf(schema, row), Before = before, After = after });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            var result = new CatalogRow(row, label ?? "");
            return action == "insert" ? TypedResults.Created($"/api/catalog/{schema.Table.Name}/rows", result) : TypedResults.Ok(result);
        }
        catch (PostgresException exception) { return Problem(exception, database, schema); }
        catch (DbUpdateException update) when (update.InnerException is PostgresException exception) { return Problem(exception, database, schema); }
    }

    private static async Task<IResult> DeleteAsync(string table, CatalogDeleteRequest request, AppDbContext db, CatalogSchemaReader reader, HttpContext context, CancellationToken ct)
    {
        var database = await reader.GetAsync(db, ct);
        if (!database.Tables.TryGetValue(table, out var schema)) { return TypedResults.NotFound(); }
        var errors = new Dictionary<string, string[]>();
        var key = CatalogValues.Key(schema, request.Key, errors);
        if (key is null) { return TypedResults.ValidationProblem(errors); }

        // Every table a cascading foreign key can reach from this one; counted before and after to report what else goes.
        var reached = new HashSet<string> { table };
        var queue = new Queue<string>([table]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var child in database.ForeignKeys.Where(fk => fk.RefTable == current && fk.OnDelete == 'c').Select(fk => fk.Table))
            {
                if (reached.Add(child)) { queue.Enqueue(child); }
            }
        }
        var countSql = string.Join(" UNION ALL ", reached.Select(name => $"SELECT '{name.Replace("'", "''")}', count(*) FROM public.{Q(name)}"));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var tx = (NpgsqlTransaction)transaction.GetDbTransaction();
        async Task<Dictionary<string, long>> CountAsync()
        {
            var counts = new Dictionary<string, long>();
            await using var command = new NpgsqlCommand(countSql, connection, tx);
            await using var result = await command.ExecuteReaderAsync(ct);
            while (await result.ReadAsync(ct)) { counts[result.GetString(0)] = result.GetInt64(1); }
            return counts;
        }
        try
        {
            var beforeCounts = await CountAsync();
            string? removed;
            var match = string.Join(" AND ", schema.Key.Select(column => $"t.{Q(column)} = k.{Q(column)}"));
            await using (var command = new NpgsqlCommand(
                $"DELETE FROM {From(schema)} AS t USING jsonb_populate_record(NULL::{From(schema)}, @key::jsonb) k WHERE {match} RETURNING to_jsonb(t)::text", connection, tx))
            {
                command.Parameters.AddWithValue("key", NpgsqlTypes.NpgsqlDbType.Jsonb, key.ToJsonString());
                removed = (string?)await command.ExecuteScalarAsync(ct);
            }
            if (removed is null) { await transaction.RollbackAsync(ct); return TypedResults.NotFound(); }
            var afterCounts = await CountAsync();
            var cascade = beforeCounts.Where(entry => entry.Key != table && entry.Value > afterCounts[entry.Key])
                .ToDictionary(entry => entry.Key, entry => entry.Value - afterCounts[entry.Key]);
            if (request.Preview) { await transaction.RollbackAsync(ct); return TypedResults.Ok(new CatalogDeleteResponse(false, cascade)); }
            db.CatalogChanges.Add(new CatalogChange { UserId = OwnedResources.UserId(context), TableName = table, Action = "delete",
                RowKey = key.ToJsonString(), Before = removed, Cascade = cascade.Count == 0 ? null : JsonSerializer.Serialize(cascade) });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return TypedResults.Ok(new CatalogDeleteResponse(true, cascade));
        }
        catch (PostgresException exception) { return Problem(exception, database, schema); }
        catch (DbUpdateException update) when (update.InnerException is PostgresException exception) { return Problem(exception, database, schema); }
    }

    private static async Task<IResult> ListChangesAsync(string? table, int? limit, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var me = OwnedResources.UserId(context);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var changes = await db.CatalogChanges.AsNoTracking().Where(change => table == null || change.TableName == table)
            .OrderByDescending(change => change.ChangedAtUtc).ThenByDescending(change => change.Id).Take(take)
            .Select(change => new { change, editor = db.Users.Where(user => user.Id == change.UserId).Select(user => new { user.DisplayName, user.Email }).FirstOrDefault() })
            .ToListAsync(ct);
        return TypedResults.Ok(changes.Select(item => new CatalogChangeResponse(item.change.Id, item.change.TableName, item.change.Action, Json(item.change.RowKey),
            item.change.Before is null ? null : Json(item.change.Before), item.change.After is null ? null : Json(item.change.After),
            item.change.Cascade is null ? null : Json(item.change.Cascade),
            item.editor is null ? null : item.editor.DisplayName ?? MaskEmail(item.editor.Email), item.change.UserId == me, item.change.ChangedAtUtc)));
    }

    /// <summary>Every signed-in user can read the log, so it never shows other people's full email: "farmer@example.com" → "fa…@example.com".</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "a user" : $"{email[..Math.Min(2, at)]}…{email[at..]}";
    }

    private static string KeyOf(CatalogSchema schema, JsonElement row) =>
        new JsonObject(schema.Key.Select(column => KeyValuePair.Create(column, JsonNode.Parse(row.GetProperty(column).GetRawText())))).ToJsonString();

    /// <summary>PostgreSQL rule violations become field errors the form can show next to the right input.</summary>
    private static IResult Problem(PostgresException exception, CatalogDatabase database, CatalogSchema schema)
    {
        string Title(string name) => CatalogTables.Find(name)?.Title ?? name;
        switch (exception.SqlState)
        {
            case PostgresErrorCodes.UniqueViolation:
            {
                var columns = schema.Uniques.FirstOrDefault(unique => unique.Name == exception.ConstraintName)?.Columns ?? schema.Key;
                var message = columns.Length == 1 ? "Another row already has this value." : $"This combination of {string.Join(", ", columns)} already exists.";
                return TypedResults.ValidationProblem(columns.ToDictionary(column => column, _ => new[] { message }));
            }
            case PostgresErrorCodes.ForeignKeyViolation:
            {
                var key = database.ForeignKeys.FirstOrDefault(fk => fk.Name == exception.ConstraintName);
                if (key is not null && key.Table == schema.Table.Name)
                {
                    return TypedResults.ValidationProblem(key.Columns.ToDictionary(column => column, _ => new[] { $"Choose an existing row of {Title(key.RefTable)}." }));
                }
                return TypedResults.Problem(statusCode: 409, title: "Still in use",
                    detail: $"{Title(key?.Table ?? "another table")} still refers to this row. Remove or change those rows first.");
            }
            case PostgresErrorCodes.CheckViolation:
            {
                var rule = exception.ConstraintName is { } name ? database.CheckRules.GetValueOrDefault(name) ?? name : "a check constraint";
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["row"] = [$"The values break a rule of this table: {rule}."] });
            }
            case PostgresErrorCodes.NotNullViolation:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [exception.ColumnName ?? "row"] = ["Required."] });
            default:
                if (exception.SqlState.StartsWith("22", StringComparison.Ordinal))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["row"] = [exception.MessageText] });
                }
                throw exception;
        }
    }
}
