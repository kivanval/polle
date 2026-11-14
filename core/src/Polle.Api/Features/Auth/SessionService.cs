using System.Security.Claims;
using Polle.Domain.Users;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Polle.Api.Features.Auth;

public sealed class SessionService(AppDbContext db, TimeProvider clock)
{
    public async Task SignInAsync(HttpContext context, User user, CancellationToken ct)
    {
        if (Guid.TryParse(context.User.FindFirstValue("sid"), out var previousId))
        {
            var previous = await db.Sessions.FindAsync([previousId], ct);
            if (previous is not null) { db.Sessions.Remove(previous); }
        }
        var now = clock.GetUtcNow();
        var session = new UserSession { UserId = user.Id, CreatedAtUtc = now.UtcDateTime, ExpiresAtUtc = now.AddDays(7).UtcDateTime };
        db.Sessions.Add(session);
        // Persists a new registration atomically with its session.
        await db.SaveChangesAsync(ct);
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("sid", session.Id.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = now.AddDays(7), AllowRefresh = false });
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken ct)
    {
        if (Guid.TryParse(context.User.FindFirstValue("sid"), out var sessionId))
        {
            var session = await db.Sessions.FindAsync([sessionId], ct);
            if (session is not null)
            {
                db.Sessions.Remove(session);
                await db.SaveChangesAsync(ct);
            }
        }
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
