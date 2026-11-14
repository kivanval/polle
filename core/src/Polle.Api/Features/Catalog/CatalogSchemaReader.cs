using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Features.Catalog;

/// <summary>How the editor treats a column: integer, decimal, boolean, text, date, timestamp, enum, json, translations or unsupported.</summary>
public sealed record CatalogColumn(string Name, string Kind, string DbType, bool Nullable, bool HasDefault, bool Generated, int? MaxLength,
    string[]? Options, CatalogLink? Link, bool Key);
public sealed record CatalogLink(string Table, string Column);
public sealed record CatalogUnique(string Name, string[] Columns);
public sealed record CatalogSchema(CatalogTable Table, string[] Key, CatalogColumn[] Columns, CatalogUnique[] Uniques, string[] Checks);
/// <summary>A foreign key anywhere in <c>public</c>. OnDelete is pg_constraint.confdeltype: c cascade, r restrict, a no action, n set null, d set default.</summary>
public sealed record CatalogForeignKey(string Name, string Table, string[] Columns, string RefTable, string[] RefColumns, char OnDelete);
public sealed record CatalogDatabase(IReadOnlyDictionary<string, CatalogSchema> Tables, CatalogForeignKey[] ForeignKeys, IReadOnlyDictionary<string, string> CheckRules);

/// <summary>
/// Reads the catalog tables' structure from PostgreSQL, so a column the data team adds shows up without a code change.
/// Cached for a minute per process.
/// </summary>
public sealed class CatalogSchemaReader
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private (CatalogDatabase Database, DateTime LoadedAt)? cached;

    public async Task<CatalogDatabase> GetAsync(AppDbContext db, CancellationToken ct)
    {
        if (cached is { } hit && DateTime.UtcNow - hit.LoadedAt < Lifetime) { return hit.Database; }
        await gate.WaitAsync(ct);
        try
        {
            if (cached is { } again && DateTime.UtcNow - again.LoadedAt < Lifetime) { return again.Database; }
            var database = await LoadAsync(db, ct);
            cached = (database, DateTime.UtcNow);
            return database;
        }
        finally { gate.Release(); }
    }

    public void Forget() => cached = null;

    private static async Task<CatalogDatabase> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var names = CatalogTables.List.Select(table => table.Name).ToArray();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var enums = new Dictionary<string, string[]>();
            await using (var command = new NpgsqlCommand("""
                SELECT t.typname, array_agg(e.enumlabel ORDER BY e.enumsortorder)::text[]
                FROM pg_type t JOIN pg_enum e ON e.enumtypid = t.oid JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'public' GROUP BY t.typname
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) { enums[reader.GetString(0)] = reader.GetFieldValue<string[]>(1); }
            }

            var keys = new List<CatalogForeignKey>();
            var primary = new Dictionary<string, string[]>();
            var uniques = new Dictionary<string, List<CatalogUnique>>();
            var checks = new Dictionary<string, List<string>>();
            var checkRules = new Dictionary<string, string>();
            await using (var command = new NpgsqlCommand("""
                SELECT rel.relname, con.contype::text, con.conname, pg_get_constraintdef(con.oid),
                       ARRAY(SELECT a.attname FROM unnest(con.conkey) WITH ORDINALITY k(n, i)
                             JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.n ORDER BY k.i)::text[],
                       ref.relname,
                       ARRAY(SELECT a.attname FROM unnest(con.confkey) WITH ORDINALITY k(n, i)
                             JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.n ORDER BY k.i)::text[],
                       con.confdeltype::text
                FROM pg_constraint con
                JOIN pg_class rel ON rel.oid = con.conrelid
                JOIN pg_namespace ns ON ns.oid = rel.relnamespace
                LEFT JOIN pg_class ref ON ref.oid = con.confrelid
                WHERE ns.nspname = 'public' AND con.contype IN ('p', 'u', 'f', 'c')
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var table = reader.GetString(0);
                    var name = reader.GetString(2);
                    var columns = reader.GetFieldValue<string[]>(4);
                    switch (reader.GetString(1))
                    {
                        case "p": primary[table] = columns; break;
                        case "u": (uniques.TryGetValue(table, out var u) ? u : uniques[table] = []).Add(new CatalogUnique(name, columns)); break;
                        case "c":
                            (checks.TryGetValue(table, out var c) ? c : checks[table] = []).Add(reader.GetString(3));
                            checkRules[name] = reader.GetString(3);
                            break;
                        case "f":
                            keys.Add(new CatalogForeignKey(name, table, columns, reader.GetString(5), reader.GetFieldValue<string[]>(6), reader.GetString(7)[0]));
                            break;
                    }
                }
            }

            var columnsByTable = names.ToDictionary(name => name, _ => new List<CatalogColumn>());
            await using (var command = new NpgsqlCommand("""
                SELECT table_name, column_name, data_type, udt_name, is_nullable = 'YES', column_default,
                       character_maximum_length, is_identity = 'YES' OR coalesce(column_default, '') LIKE 'nextval(%'
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = ANY(@tables)
                ORDER BY table_name, ordinal_position
                """, connection))
            {
                command.Parameters.AddWithValue("tables", names);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var table = reader.GetString(0);
                    var column = reader.GetString(1);
                    var udt = reader.GetString(3);
                    var definition = CatalogTables.Find(table)!;
                    var link = keys.FirstOrDefault(key => key.Table == table && key.Columns.Length == 1 && key.Columns[0] == column);
                    var kind = enums.ContainsKey(udt) ? "enum" : udt switch
                    {
                        "int2" or "int4" or "int8" => "integer",
                        "numeric" or "float4" or "float8" => "decimal",
                        "bool" => "boolean",
                        "varchar" or "bpchar" or "text" => "text",
                        "date" => "date",
                        "timestamp" or "timestamptz" => "timestamp",
                        "json" or "jsonb" => definition.Translations.Contains(column) ? "translations" : "json",
                        _ => "unsupported",
                    };
                    columnsByTable[table].Add(new CatalogColumn(column, kind, udt, reader.GetBoolean(4), !reader.IsDBNull(5),
                        reader.GetBoolean(7), reader.IsDBNull(6) ? null : reader.GetInt32(6), enums.GetValueOrDefault(udt),
                        link is null ? null : new CatalogLink(link.RefTable, link.RefColumns[0]),
                        primary.GetValueOrDefault(table)?.Contains(column) == true));
                }
            }

            var tables = CatalogTables.List
                .Where(table => columnsByTable[table.Name].Count > 0 && primary.ContainsKey(table.Name))
                .ToDictionary(table => table.Name, table => new CatalogSchema(table, primary[table.Name], [.. columnsByTable[table.Name]],
                    [.. uniques.GetValueOrDefault(table.Name) ?? []], [.. checks.GetValueOrDefault(table.Name) ?? []]));
            return new CatalogDatabase(tables, [.. keys], checkRules);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
