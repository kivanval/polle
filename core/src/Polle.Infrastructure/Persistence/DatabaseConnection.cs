using Npgsql;

namespace Polle.Infrastructure.Persistence;

public static class DatabaseConnection
{
    public static string Normalize(string value)
    {
        try
        {
            if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                return new NpgsqlConnectionStringBuilder(value).ConnectionString;
            }
            var uri = new Uri(value);
            var credentials = uri.UserInfo.Split(':', 2);
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
                SslMode = SslMode.VerifyFull,
                Timeout = 15
            };
            foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = parameter.Split('=', 2);
                if (pair.Length != 2) { continue; }
                var setting = Uri.UnescapeDataString(pair[1]);
                switch (pair[0].ToLowerInvariant())
                {
                    case "sslmode": builder.SslMode = setting.Equals("require", StringComparison.OrdinalIgnoreCase)
                        ? SslMode.VerifyFull : Enum.Parse<SslMode>(setting.Replace("-", ""), true); break;
                    case "channel_binding": builder["Channel Binding"] = setting; break;
                    case "connect_timeout": builder.Timeout = int.Parse(setting, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "application_name": builder.ApplicationName = setting; break;
                }
            }
            return builder.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("Database connection format is invalid. Use an Npgsql connection string or a PostgreSQL URI. Details suppressed.");
        }
    }
}
