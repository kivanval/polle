using Polle.Api.Features.Strategies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Polle.Api.Tests;

/// <summary>The felder client is built by Program.cs from Solver:BaseUrl.</summary>
public sealed class SolverConfigurationTests
{
    private static FelderClient Client(string? baseUrl = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("DataProtection:PersistKeysToDatabase", "false");
            if (baseUrl is not null) builder.UseSetting("Solver:BaseUrl", baseUrl);
        });
        return factory.Services.CreateScope().ServiceProvider.GetRequiredService<FelderClient>();
    }

    [Fact]
    public void The_permanent_solver_address_is_the_default()
    {
        var client = Client();

        Assert.True(client.Configured);
        Assert.Equal(new Uri("https://opt-felder-api.pp.ua/"), client.BaseAddress);
    }

    [Fact]
    public void An_override_without_a_trailing_slash_still_keeps_the_api_path()
    {
        var client = Client("https://solver.example/base");

        Assert.Equal(new Uri("https://solver.example/base/"), client.BaseAddress);
    }
}
