using System.Security.Claims;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Auth;

public sealed class SessionCookieEvents(AppDbContext db, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var validIds = Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId)
            & Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var now = clock.GetUtcNow().UtcDateTime;
        if (!validIds || !await db.Sessions.AsNoTracking().AnyAsync(
            session => session.Id == sessionId && session.UserId == userId && session.ExpiresAtUtc > now,
            context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
