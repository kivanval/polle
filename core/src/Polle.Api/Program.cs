using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Polle.Api.Common;
using Polle.Api.Features.Auth;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Drought;
using Polle.Api.Features.Finance;
using Polle.Api.Features.Solver;
using Polle.Api.Features.Strategies;
using Polle.Api.Features.Weather;
using Polle.Domain.Users;
using Polle.Infrastructure;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;
using NetTopologySuite.IO.Converters;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
// Log sanitized failure types through ApiExceptionHandler, not database driver details.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
// Default HttpClient information logs include the query string and therefore the field point.
builder.Logging.AddFilter("System.Net.Http.HttpClient.NasaPowerHistoricalWeather", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.NasaPowerDailyClimate", LogLevel.None);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddOpenApi(options => options.AddSchemaTransformer((schema, context, _) =>
{
    // Nullable DTO properties let manual validation return field-specific errors, but [Required]
    // inputs must not advertise JSON null as a valid value to generated clients.
    if (context.JsonPropertyInfo?.AttributeProvider?.IsDefined(typeof(RequiredAttribute), true) == true)
    {
        schema.Type &= ~JsonSchemaType.Null;
    }
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EcoCrop");
// Production keeps the key ring in PostgreSQL (table data_protection_keys) so session and antiforgery cookies
// survive restarts and redeploys; local development keeps the default per-machine key store.
if (builder.Configuration.GetValue("DataProtection:PersistKeysToDatabase", builder.Environment.IsProduction()))
{
    dataProtection.PersistKeysToDbContext<AppDbContext>();
    // The key ring is encrypted with a certificate held outside the database, so read access to the
    // database (a backup or a branch) is not enough to forge session cookies.
    var certificate = builder.Configuration["DataProtection:CertificateBase64"];
    if (!string.IsNullOrEmpty(certificate))
        dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificate), null));
    else if (builder.Environment.IsProduction())
        throw new InvalidOperationException("DataProtection:CertificateBase64 must be configured in production.");
}
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddSingleton<DummyPassword>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddScoped<SolverRequestBuilder>();
builder.Services.AddScoped<IMonthlyWeatherProvider, UnavailableMonthlyWeatherProvider>();
builder.Services.AddSingleton<HistoricalWeatherCache>();
builder.Services.AddSingleton<Polle.Api.Features.Catalog.CatalogSchemaReader>();
builder.Services.AddTransient<ClimateScenarioService>();
builder.Services.AddSingleton<ClimateTrendStore>();
builder.Services.AddHttpClient<NasaPowerHistoricalWeather>((services, client) =>
{
    var address = services.GetRequiredService<IConfiguration>()["Weather:NasaPowerBaseUrl"]
        ?? "https://power.larc.nasa.gov/";
    client.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddSingleton<DailyClimateBaselineCache>();
builder.Services.AddHttpClient<NasaPowerDailyClimate>((services, client) =>
{
    var address = services.GetRequiredService<IConfiguration>()["Weather:NasaPowerBaseUrl"]
        ?? "https://power.larc.nasa.gov/";
    client.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
    // Ten years of daily values is a larger response than a monthly read; each attempt also has its own 20 s budget.
    client.Timeout = TimeSpan.FromSeconds(25);
});
// felder solver (Solver:BaseUrl). Unset leaves the client without an address and the strategy routes answer 503.
// Its status route signals completion with 303, so redirects are read, not followed.
builder.Services.AddHttpClient<FelderClient>((services, client) =>
{
    var address = services.GetRequiredService<IConfiguration>()["Solver:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(address)) client.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
    // HttpClient sends no User-Agent by default; Cloudflare in front of the solver scores such requests as bots.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Polle-Api/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
// public.materials id that prices irrigation water for the solver; felder picks fertilizer products itself.
builder.Services.AddSingleton(services =>
    new SolverMaterials(services.GetRequiredService<IConfiguration>()["Solver:Materials:Irrigation"] ?? "2215"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "ecocrop.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = false;
    options.EventsType = typeof(SessionCookieEvents);
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "ecocrop.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Too many attempts", detail: "Please wait a minute before trying again.").ExecuteAsync(context.HttpContext);
    };
    // Login and register, per client IP. Generous enough for a team testing from one office IP,
    // still a wall against password guessing.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Public drought map: a day's browsing reads a few overlays and point values, far below this limit.
    options.AddPolicy("public-data", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Only requests with the solver's key get this far, so they share one partition no anonymous traffic can use up.
    // One strategy run starts several solver jobs at once and each reads finance, climate and fields,
    // so these partitions need room for a few runs in the same minute.
    options.AddPolicy("finance", _ => RateLimitPartition.GetFixedWindowLimiter(
        "solver", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // The solver's climate and field reads, each with its own key and partition, like finance.
    options.AddPolicy("climate", _ => RateLimitPartition.GetFixedWindowLimiter(
        "solver-climate", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("fields", _ => RateLimitPartition.GetFixedWindowLimiter(
        "solver-fields", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Historical weather, climate scenarios and solver drafts call NASA POWER upstream, so this one stays the tightest.
    options.AddPolicy("weather", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
// Same-origin Vite proxy by default; credentialed CORS only for explicit origins.
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (origins.Length > 0) { policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Content-Type", "X-CSRF-TOKEN").AllowCredentials(); }
}));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new GeoJsonConverterFactory()));

// In production the API is reachable only through the frontend's same-origin proxy, which authenticates
// itself with a shared secret. Only then are its client IP and HTTPS scheme trusted; spoofable
// X-Forwarded-* headers are never read.
var proxySecret = builder.Configuration["Proxy:SharedSecret"];
if (builder.Environment.IsProduction() && (proxySecret?.Length ?? 0) < 32)
    throw new InvalidOperationException("Proxy:SharedSecret must be configured with at least 32 characters in production.");

var app = builder.Build();
// Only the solver may read prices: it reaches this route directly with its own key, and the frontend's proxy must not
// open it to browsers. Without a key the route is open in Development only, and closed everywhere else.
// Routing has already run here, so every path spelling that reaches the route is covered, such as a trailing slash.
var financeKey = builder.Configuration["Finance:ApiKey"];
var financeOpen = string.IsNullOrEmpty(financeKey) && app.Environment.IsDevelopment();
var expectedFinanceKey = SHA256.HashData(Encoding.UTF8.GetBytes(financeKey ?? ""));
var financeKeySet = !string.IsNullOrEmpty(financeKey);
app.Use(async (context, next) =>
{
    if (FinanceEndpoints.IsFinanceRoute(context) && !financeOpen)
    {
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[FinanceEndpoints.ApiKeyHeader].ToString()));
        if (!financeKeySet || !CryptographicOperations.FixedTimeEquals(supplied, expectedFinanceKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next(context);
});
// The solver's climate and field topology routes follow the same rule, each with its own key (Climate:ApiKey,
// Fields:ApiKey) named by the route's SolverServiceKey metadata.
app.Use(async (context, next) =>
{
    if (SolverServiceKey.Of(context) is { } route && !SolverServiceKey.Allows(app.Configuration[route.ConfigurationKey],
            context.Request.Headers[route.Header].ToString(), app.Environment.IsDevelopment()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next(context);
});
if (!string.IsNullOrEmpty(proxySecret))
{
    var expectedSecret = SHA256.HashData(Encoding.UTF8.GetBytes(proxySecret));
    app.Use(async (context, next) =>
    {
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-EcoCrop-Proxy-Secret"].ToString()));
        if (!CryptographicOperations.FixedTimeEquals(supplied, expectedSecret))
        {
            // The hosting platform's health probe reaches the container directly.
            if (context.Request.Path.Equals("/api/health/live", StringComparison.OrdinalIgnoreCase)) { await next(context); return; }
            // The solver backend calls these read-only routes directly, not through the frontend's proxy; its keys were checked above.
            if (FinanceEndpoints.IsFinanceRoute(context) || SolverServiceKey.Of(context) is not null)
            {
                context.Request.Scheme = "https";
                await next(context);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (IPAddress.TryParse(context.Request.Headers["X-EcoCrop-Client-IP"].ToString(), out var client))
            context.Connection.RemoteIpAddress = client;
        context.Request.Scheme = "https";
        await next(context);
    });
}
app.UseExceptionHandler();
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode).ExecuteAsync(context.HttpContext));
if (app.Environment.IsProduction()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
        // Authenticated by a key header, not a cookie, so a forged browser request has nothing to ride on.
        && !FinanceEndpoints.IsFinanceRoute(context))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(statusCode: 400, title: "Security check failed", detail: "Refresh the page and try again.").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.MapScalarApiReference(); }
var api = app.MapGroup("/api");
api.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
api.MapHealthChecks("/health");
api.MapEndpoints();
app.Run();
