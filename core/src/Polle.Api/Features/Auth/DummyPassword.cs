using Polle.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Polle.Api.Features.Auth;

public sealed class DummyPassword
{
    public User User { get; } = new() { Email = "unused@invalid.example", PasswordHash = "" };
    public DummyPassword(IPasswordHasher<User> hasher)
    {
        User.PasswordHash = hasher.HashPassword(User, Guid.NewGuid().ToString("N"));
    }
}
