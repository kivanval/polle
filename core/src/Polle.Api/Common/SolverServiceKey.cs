using System.Security.Cryptography;
using System.Text;

namespace Polle.Api.Common;

/// <summary>
/// Route metadata for a read-only service the solver (felder) calls directly. The solver has no session, so it sends
/// its own key in <see cref="Header"/>, compared with the setting <see cref="ConfigurationKey"/>; the route needs neither
/// the proxy secret nor a CSRF token. Program.cs checks the key after routing, so it goes by the route the request
/// resolved to, not by the path text. Finance keeps its own, earlier check (FinanceEndpoints.IsFinanceRoute).
/// </summary>
public sealed record SolverServiceKey(string Header, string ConfigurationKey)
{
    public static SolverServiceKey? Of(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<SolverServiceKey>();

    /// <summary>
    /// Whether a request may reach the route. Without a configured key the route is open in Development and closed
    /// everywhere else, as for finance.
    /// </summary>
    public static bool Allows(string? configuredKey, string suppliedKey, bool isDevelopment)
    {
        if (string.IsNullOrEmpty(configuredKey)) return isDevelopment;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)));
    }
}
