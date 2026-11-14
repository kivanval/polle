using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Polle.Api.Tests;

/// <summary>
/// Runs the real Program.cs pipeline (routing, key checks, proxy secret, binding, validation) without a database: each
/// case is answered before the field lookup. Database-backed answers are covered by the end-to-end run in the report.
/// </summary>
public sealed class SolverRoutesTests
{
    private const string ClimateKey = "climate-test-key";
    private const string FieldsKey = "fields-test-key";
    private const string ProxySecret = "proxy-secret-proxy-secret-proxy-secret";
    private const string FieldId = "3f1c2a9e-0b7d-4c55-9a51-2f0e6d1c8b40";

    private static HttpClient Client(string environment = "Staging", bool keys = true, bool proxy = false)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("DataProtection:PersistKeysToDatabase", "false");
            if (keys)
            {
                builder.UseSetting("Climate:ApiKey", ClimateKey);
                builder.UseSetting("Fields:ApiKey", FieldsKey);
                builder.UseSetting("Finance:ApiKey", "finance-test-key");
            }
            if (proxy) builder.UseSetting("Proxy:SharedSecret", ProxySecret);
        });
        return factory.CreateClient();
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string? header = null, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (header is not null) request.Headers.Add(header, key);
        return client.SendAsync(request);
    }

    private const string ClimatePath = $"/api/v1/climate/daily/{FieldId}?start_date=2026-04-01&end_date=2026-10-31";
    private const string FieldsPath = $"/api/v1/fields/{FieldId}/2026-04";

    [Theory]
    [InlineData(ClimatePath, null, null)]
    [InlineData(ClimatePath, "X-Climate-Api-Key", "wrong")]
    [InlineData(ClimatePath, "X-Fields-Api-Key", FieldsKey)]
    [InlineData(FieldsPath, null, null)]
    [InlineData(FieldsPath, "X-Fields-Api-Key", "wrong")]
    [InlineData(FieldsPath, "X-Climate-Api-Key", ClimateKey)]
    [InlineData($"/api/v1/fields/{FieldId}/2026-04/", null, null)]
    [InlineData($"/API/V1/CLIMATE/DAILY/{FieldId}/?start_date=2026-04-01&end_date=2026-10-31", null, null)]
    public async Task A_missing_or_wrong_key_is_rejected(string path, string? header, string? key)
    {
        using var response = await Get(Client(), path, header, key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_configured_key_the_routes_are_closed_outside_development()
    {
        var client = Client(keys: false);

        using var climate = await Get(client, ClimatePath, "X-Climate-Api-Key", "");
        using var fields = await Get(client, FieldsPath, "X-Fields-Api-Key", "");

        Assert.Equal(HttpStatusCode.Unauthorized, climate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fields.StatusCode);
    }

    [Fact]
    public async Task Without_a_configured_key_the_routes_are_open_in_development()
    {
        var client = Client("Development", keys: false);

        using var climate = await Get(client, $"/api/v1/climate/daily/{FieldId}?start_date=bad&end_date=2026-10-31");
        using var fields = await Get(client, $"/api/v1/fields/{FieldId}/2026-13");

        Assert.Equal(HttpStatusCode.BadRequest, climate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fields.StatusCode);
    }

    [Fact]
    public async Task Climate_validates_snake_case_query_parameters()
    {
        var client = Client();

        using var missing = await Get(client, $"/api/v1/climate/daily/{FieldId}", "X-Climate-Api-Key", ClimateKey);
        using var camelCase = await Get(client, $"/api/v1/climate/daily/{FieldId}?startDate=2026-04-01&endDate=2026-10-31",
            "X-Climate-Api-Key", ClimateKey);
        using var reversed = await Get(client, $"/api/v1/climate/daily/{FieldId}?start_date=2026-10-31&end_date=2026-04-01",
            "X-Climate-Api-Key", ClimateKey);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, camelCase.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        var body = await reversed.Content.ReadAsStringAsync();
        Assert.Contains("start_date", body);
    }

    [Fact]
    public async Task Field_topology_validates_year_month()
    {
        using var response = await Get(Client(), $"/api/v1/fields/{FieldId}/2026-4", "X-Fields-Api-Key", FieldsKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("year_month", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_id_that_is_not_an_polle_field_is_not_found()
    {
        var client = Client();

        using var climate = await Get(client, "/api/v1/climate/daily/fld_01?start_date=2026-04-01&end_date=2026-10-31",
            "X-Climate-Api-Key", ClimateKey);
        using var fields = await Get(client, "/api/v1/fields/fld_01/2026-04", "X-Fields-Api-Key", FieldsKey);

        Assert.Equal(HttpStatusCode.NotFound, climate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fields.StatusCode);
    }

    [Fact]
    public async Task The_solver_reaches_its_routes_without_the_proxy_secret_but_nothing_else()
    {
        var client = Client(proxy: true);

        using var climate = await Get(client, "/api/v1/climate/daily/fld_01?start_date=2026-04-01&end_date=2026-10-31",
            "X-Climate-Api-Key", ClimateKey);
        using var fields = await Get(client, "/api/v1/fields/fld_01/2026-04", "X-Fields-Api-Key", FieldsKey);
        using var climateNoKey = await Get(client, ClimatePath);
        using var other = await Get(client, "/api/farms");

        // 404 from the handler (unknown id), not from the proxy gate: the request got through.
        Assert.Equal(HttpStatusCode.NotFound, climate.StatusCode);
        Assert.Contains("application/problem+json", climate.Content.Headers.ContentType?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, fields.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, climateNoKey.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Null(other.Content.Headers.ContentType);
    }

    [Fact]
    public async Task Finance_still_requires_its_own_key()
    {
        var client = Client();
        var body = new StringContent("""{"start_year":2026,"end_year":2027,"material_ids":[]}""", Encoding.UTF8, "application/json");

        using var noKey = await client.PostAsync("/api/v1/finance/prices", body);
        using var climateKey = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/prices")
        {
            Content = new StringContent("""{"start_year":2026,"end_year":2027,"material_ids":[]}""", Encoding.UTF8, "application/json"),
            Headers = { { "X-Climate-Api-Key", ClimateKey } }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, climateKey.StatusCode);
    }

    [Theory]
    [InlineData("/api/v2/finance/materials/prices")]
    [InlineData("/api/v2/finance/crop-profiles/opex")]
    public async Task Finance_v2_routes_require_the_finance_key(string path)
    {
        var client = Client();
        using var noKey = await client.PostAsync(path, new StringContent("""{"start_year":2026,"end_year":2027,"ids":[]}""", Encoding.UTF8, "application/json"));
        using var climateKey = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("""{"start_year":2026,"end_year":2027,"ids":[]}""", Encoding.UTF8, "application/json"),
            Headers = { { "X-Climate-Api-Key", ClimateKey } }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, climateKey.StatusCode);
    }
}
