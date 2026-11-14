using System.ComponentModel.DataAnnotations;

namespace Polle.Api.Features.Auth;

public static class AuthValidation
{
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static Dictionary<string, string[]> Validate(string? email, string? password, string? confirmation = null, bool registering = false)
    {
        var errors = new Dictionary<string, string[]>();
        var normalized = NormalizeEmail(email);
        if (normalized.Length is 0 or > 254 || !new EmailAddressAttribute().IsValid(normalized))
        {
            errors["email"] = ["Enter a valid email address (up to 254 characters)."];
        }
        if (password is null || password.Length is 0 or > 128)
        {
            errors["password"] = ["Enter a password of up to 128 characters."];
        }
        else if (registering && (password.Length < 12 || !password.Any(char.IsAsciiLetterUpper)
            || !password.Any(char.IsAsciiLetterLower) || !password.Any(char.IsAsciiDigit)))
        {
            errors["password"] = ["Use 12–128 characters, including uppercase, lowercase and a number."];
        }
        if (registering && (confirmation is null || password != confirmation))
        {
            errors["confirmPassword"] = ["Your passwords do not match."];
        }
        return errors;
    }
}
