using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Polle.Api.Features.Catalog;

/// <summary>
/// Checks submitted values against the column kinds before they reach PostgreSQL, so errors name the column.
/// The database still enforces keys, links, uniqueness and check constraints.
/// </summary>
internal static partial class CatalogValues
{
    [GeneratedRegex("^[a-z]{2}(-[A-Z]{2})?$")]
    private static partial Regex Language();

    public static JsonObject Clean(CatalogSchema schema, JsonObject? values, bool insert, Dictionary<string, string[]> errors)
    {
        var clean = new JsonObject();
        values ??= [];
        foreach (var (name, _) in values)
        {
            if (schema.Columns.All(column => column.Name != name)) { errors[name] = ["This table has no such column."]; }
        }
        foreach (var column in schema.Columns)
        {
            var given = values.TryGetPropertyValue(column.Name, out var node);
            // Generated keys are set by the database; key columns identify the row and are never rewritten.
            if (column.Generated || (!insert && column.Key)) { continue; }
            if (!given)
            {
                if (insert && !column.Nullable && !column.HasDefault) { errors[column.Name] = ["Required."]; }
                continue;
            }
            if (Convert(column, node, errors) is var (ok, value) && ok) { clean[column.Name] = value; }
        }
        return clean;
    }

    public static JsonObject? Key(CatalogSchema schema, JsonObject? key, Dictionary<string, string[]> errors)
    {
        if (key is null || key.Count != schema.Key.Length || schema.Key.Any(name => !key.ContainsKey(name)))
        {
            errors["key"] = [$"Identify the row by {string.Join(" and ", schema.Key)}."];
            return null;
        }
        var clean = new JsonObject();
        foreach (var name in schema.Key)
        {
            var column = schema.Columns.Single(candidate => candidate.Name == name);
            if (key[name] is null) { errors["key"] = ["Key values cannot be empty."]; return null; }
            if (Convert(column, key[name], errors) is var (ok, value) && ok) { clean[name] = value; }
        }
        return errors.Count == 0 ? clean : null;
    }

    private static (bool Ok, JsonNode? Value) Convert(CatalogColumn column, JsonNode? node, Dictionary<string, string[]> errors)
    {
        (bool, JsonNode?) Fail(string message) { errors[column.Name] = [message]; return (false, null); }
        if (node is null || (node is JsonValue blank && blank.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text) && column.Kind is not "translations" and not "json"))
        {
            return column.Nullable ? (true, null) : Fail("Required.");
        }
        var element = node.Deserialize<JsonElement>();
        string? Raw() => element.ValueKind switch { JsonValueKind.String => element.GetString()!.Trim(), JsonValueKind.Number => element.GetRawText(), _ => null };
        switch (column.Kind)
        {
            case "integer":
            {
                if (!long.TryParse(Raw(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) { return Fail("Enter a whole number."); }
                var (min, max) = column.DbType switch { "int2" => (short.MinValue, (long)short.MaxValue), "int4" => (int.MinValue, int.MaxValue), _ => (long.MinValue, long.MaxValue) };
                return number < min || number > max ? Fail($"Enter a whole number between {min} and {max}.") : (true, JsonValue.Create(number));
            }
            case "decimal":
            {
                var raw = Raw()?.Replace(',', '.');
                return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? (true, JsonValue.Create(number)) : Fail("Enter a number, using a dot for decimals.");
            }
            case "boolean":
                return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? (true, JsonValue.Create(element.GetBoolean())) : Fail("Choose yes or no.");
            case "text":
            {
                if (element.ValueKind != JsonValueKind.String) { return Fail("Enter text."); }
                var value = element.GetString()!.Trim();
                return column.MaxLength is { } max && value.Length > max ? Fail($"Use at most {max} characters.") : (true, JsonValue.Create(value));
            }
            case "date":
                return DateOnly.TryParseExact(Raw(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? (true, JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) : Fail("Enter a date as YYYY-MM-DD.");
            case "timestamp":
                return DateTimeOffset.TryParse(Raw(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
                    ? (true, JsonValue.Create(moment.ToString("O"))) : Fail("Enter a date and time.");
            case "enum":
                return Raw() is { } option && column.Options!.Contains(option) ? (true, JsonValue.Create(option)) : Fail($"Choose one of: {string.Join(", ", column.Options!)}.");
            case "translations":
            {
                if (element.ValueKind != JsonValueKind.Object) { return Fail("Enter the name in at least one language."); }
                var names = new JsonObject();
                foreach (var entry in element.EnumerateObject())
                {
                    if (!Language().IsMatch(entry.Name)) { return Fail($"\"{entry.Name}\" is not a language code like en or uk."); }
                    if (entry.Value.ValueKind != JsonValueKind.String) { return Fail("Each translation must be text."); }
                    var value = entry.Value.GetString()!.Trim();
                    if (value.Length > 200) { return Fail("Keep each translation under 200 characters."); }
                    if (value.Length > 0) { names[entry.Name] = value; }
                }
                return names.ContainsKey("en") ? (true, names) : Fail("Add the English name. Other languages are optional.");
            }
            case "json":
                return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? (true, JsonNode.Parse(element.GetRawText())) : Fail("Enter a JSON object or list.");
            default:
                return Fail("This column can't be edited here.");
        }
    }
}
