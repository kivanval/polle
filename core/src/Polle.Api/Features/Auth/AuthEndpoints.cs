using System.Security.Claims;
using Polle.Api.Common;
using Polle.Domain.Users;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Features.Auth;

public sealed class AuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Authentication");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        auth.MapGet("/me", CurrentUserAsync).RequireAuthorization();
        auth.MapPost("/register", RegisterAsync).RequireRateLimiting("auth");
        auth.MapPost("/login", LoginAsync).RequireRateLimiting("auth");
        auth.MapPost("/logout", async (HttpContext context, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.SignOutAsync(context, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
    private static async Task<IResult> RegisterAsync(RegisterRequest request, AppDbContext db,
        IPasswordHasher<User> hasher, SessionService sessions, HttpContext context, CancellationToken ct)
    {
        var errors = AuthValidation.Validate(request.Email, request.Password, request.ConfirmPassword, true);
        if (errors.Count > 0) { return Results.ValidationProblem(errors); }
        var email = AuthValidation.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(user => user.Email == email, ct)) { return DuplicateEmail(); }
        var user = new User { Email = email, PasswordHash = "" };
        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        db.Users.Add(user);
        try { await sessions.SignInAsync(context, user, ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateEmail();
        }
        return Results.Created("/api/auth/me", UserResponse.From(user));
    }
    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db,
        IPasswordHasher<User> hasher, DummyPassword dummy, SessionService sessions, HttpContext context, CancellationToken ct)
    {
        var errors = AuthValidation.Validate(request.Email, request.Password);
        if (errors.Count > 0) { return Results.ValidationProblem(errors); }
        var email = AuthValidation.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(user => user.Email == email, ct);
        var result = hasher.VerifyHashedPassword(user ?? dummy.User, user?.PasswordHash ?? dummy.User.PasswordHash, request.Password!);
        if (user is null || result == PasswordVerificationResult.Failed)
        {
            return Results.Problem(statusCode: 401, title: "Sign-in failed", detail: "The email or password is incorrect.");
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            user.UpdatedAtUtc = DateTime.UtcNow;
        }
        await sessions.SignInAsync(context, user, ct);
        return Results.Ok(UserResponse.From(user));
    }
    private static async Task<IResult> CurrentUserAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(UserResponse.From(user));
    }
    private static IResult DuplicateEmail() => Results.Problem(statusCode: 409,
        title: "Account already exists", detail: "An account with this email already exists. Please log in.");
    public sealed record RegisterRequest(string? Email, string? Password, string? ConfirmPassword);
    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record UserResponse(Guid Id, string Email, string? DisplayName, DateTime CreatedAtUtc)
    {
        public static UserResponse From(User user) => new(user.Id, user.Email, user.DisplayName, user.CreatedAtUtc);
    }
}
