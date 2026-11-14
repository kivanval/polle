using Microsoft.Extensions.Configuration;
using Npgsql;
using Polle.Infrastructure.Persistence;

// Read-only schema preflight. Never prints connection values or driver exception messages.
var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Polle.Api"));
var config = new ConfigurationBuilder().SetBasePath(configPath)
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables().Build();
try
{
    var connectionString = config.GetConnectionString("Database");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Database is not configured. Set ConnectionStrings__Database or appsettings.Local.json.");
        return 1;
    }
    var settings = new NpgsqlConnectionStringBuilder(DatabaseConnection.Normalize(connectionString));
    await using var connection = new NpgsqlConnection(settings.ConnectionString);
    await connection.OpenAsync();
    Console.WriteLine("PostgreSQL connection verified. Local JSON is explicitly loaded; environment variables take precedence.");
    Console.WriteLine($"Configured target: {settings.Host} / {settings.Database}. Credentials suppressed.");
    await using var command = new NpgsqlCommand("SELECT table_schema, table_name FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY table_schema, table_name", connection);
    await using var reader = await command.ExecuteReaderAsync();
    var count = 0;
    while (await reader.ReadAsync()) { Console.WriteLine($"Table: {reader.GetString(0)}.{reader.GetString(1)}"); count++; }

    await reader.DisposeAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction)) { await readOnly.ExecuteNonQueryAsync(); }
    await using var nameCommand = new NpgsqlCommand("SELECT to_regclass('public.users') IS NOT NULL", connection, transaction);
    var renamed = (bool)(await nameCommand.ExecuteScalarAsync())!;
    var usersTable = renamed ? "public.users" : "public.polle_users";
    var sessionsTable = renamed ? "public.sessions" : "public.polle_sessions";
    await using var agricultureCommand = new NpgsqlCommand("SELECT to_regclass('public.farms') IS NOT NULL AND to_regclass('public.fields') IS NOT NULL AND to_regclass('public.field_plantings') IS NOT NULL", connection, transaction);
    var agricultureAvailable = (bool)(await agricultureCommand.ExecuteScalarAsync())!;
    var inspections = new (string Title, string Sql)[]
    {
        ("Target identity", "SELECT current_database(), current_user, current_setting('neon.branch_id', true), current_setting('neon.endpoint_id', true)"),
        ("Application counts", "SELECT 'farms' AS entity, count(*) FROM public.farms UNION ALL SELECT 'fields', count(*) FROM public.fields UNION ALL SELECT 'plantings', count(*) FROM public.field_plantings"),
        ("Planting periods and identifiers", "SELECT id, field_id, crop_id, season_year, planting_date, harvesting_date FROM public.field_plantings ORDER BY field_id, season_year, planting_date, id"),
        ("Solver identifier quality", "SELECT count(*) AS total, min(id), max(id), count(DISTINCT scientific_name) AS distinct_names, count(*) FILTER (WHERE scientific_name IS NULL OR btrim(scientific_name) = '') AS unnamed FROM solver.crops"),
        ("Period extension availability", "SELECT name, installed_version FROM pg_available_extensions WHERE name = 'btree_gist'"),
        ("Extensions", "SELECT name, default_version, installed_version FROM pg_available_extensions WHERE name = 'postgis'"),
        ("Columns", "SELECT table_schema, table_name, column_name, data_type, is_nullable, column_default FROM information_schema.columns WHERE (table_schema = 'public' AND table_name IN ('users','sessions','polle_users','polle_sessions','farms','fields','field_plantings','crops','__ecocrop_migrations')) OR (table_schema IN ('lakehouse','solver') AND table_name = 'crops') ORDER BY table_schema, table_name, ordinal_position"),
        ("Constraints", "SELECT conrelid::regclass::text AS relation, conname, pg_get_constraintdef(oid) AS definition FROM pg_constraint WHERE connamespace IN ('public'::regnamespace,'lakehouse'::regnamespace,'solver'::regnamespace) ORDER BY 1,2"),
        ("Indexes", "SELECT schemaname, tablename, indexname, indexdef FROM pg_indexes WHERE schemaname='public' OR (schemaname IN ('lakehouse','solver') AND tablename='crops') ORDER BY 1,2,3"),
        ("Roles", "SELECT rolname, rolsuper, rolcanlogin FROM pg_roles WHERE rolname NOT LIKE 'pg_%' ORDER BY 1"),
        ("Owners and grants", "SELECT n.nspname, c.relname, pg_get_userbyid(c.relowner) AS owner, c.relacl::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','v') ORDER BY 2"),
        ("Dependencies", "SELECT pg_describe_object(classid,objid,objsubid) AS dependent, pg_describe_object(refclassid,refobjid,refobjsubid) AS referenced, deptype FROM pg_depend WHERE refobjid IN (to_regclass('public.polle_users'),to_regclass('public.polle_sessions'),to_regclass('public.users'),to_regclass('public.sessions')) ORDER BY 1"),
        ("Triggers", "SELECT event_object_table, trigger_name, action_statement FROM information_schema.triggers WHERE event_object_schema='public'"),
        ("Functions referencing old names", "SELECT n.nspname, p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE p.prosrc LIKE '%polle_users%' OR p.prosrc LIKE '%polle_sessions%'"),
        ("Connected consumers (no query text)", "SELECT usename, application_name, state, count(*) FROM pg_stat_activity WHERE datname=current_database() GROUP BY 1,2,3"),
        ("Lakehouse crops sample", "SELECT crop_id, crop FROM lakehouse.crops ORDER BY crop_id"),
        ("Solver crops sample", "SELECT row_to_json(c)::text FROM (SELECT * FROM solver.crops LIMIT 8) c"),
        ("Crop source quality", "SELECT count(*) AS total, count(DISTINCT crop_id) AS distinct_ids, count(*) FILTER (WHERE crop_id IS NULL OR btrim(crop_id) = '' OR crop IS NULL OR btrim(crop) = '') AS invalid_rows FROM lakehouse.crops"),
        ("Crop counts", "SELECT 'lakehouse' AS catalog, count(*) FROM lakehouse.crops UNION ALL SELECT 'solver', count(*) FROM solver.crops"),
        ("Authentication counts", $"SELECT 'users' AS entity, count(*) FROM {usersTable} UNION ALL SELECT 'sessions', count(*) FROM {sessionsTable}"),
        ("Migration history", "SELECT * FROM public.__ecocrop_migrations ORDER BY 1")
    };
    foreach (var inspection in inspections)
    {
        Console.WriteLine($"--- {inspection.Title} ---");
        if (!agricultureAvailable && inspection.Title is "Application counts" or "Planting periods and identifiers")
        {
            Console.WriteLine("Agricultural tables are not installed yet; apply the reviewed prerequisite migrations first.");
            continue;
        }
        await using var inspectionCommand = new NpgsqlCommand(inspection.Sql, connection, transaction);
        await using var rows = await inspectionCommand.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            Console.WriteLine(string.Join(" | ", Enumerable.Range(0, rows.FieldCount).Select(i => rows.IsDBNull(i) ? "NULL" : rows.GetValue(i).ToString())));
        }
    }
    await transaction.RollbackAsync();
    Console.WriteLine($"Read-only preflight complete: {count} tables/views inspected. No schema changes made.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Database preflight failed ({exception.GetType().Name}). Check local configuration and connectivity. Connection details suppressed.");
    return 1;
}
