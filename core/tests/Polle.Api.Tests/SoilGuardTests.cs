using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Fields;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Microsoft.AspNetCore.Http.HttpResults;
using NetTopologySuite.Geometries;

namespace Polle.Api.Tests;

public sealed class SoilGuardTests
{
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    private static Polygon Square(double longitude, double latitude) => Wgs84.CreatePolygon(
    [
        new(longitude, latitude), new(longitude + 0.05, latitude), new(longitude + 0.05, latitude + 0.05),
        new(longitude, latitude + 0.05), new(longitude, latitude),
    ]);

    [Fact]
    public void A_run_on_a_field_without_soil_is_refused_with_422_and_the_agreed_message()
    {
        var result = Assert.IsType<ProblemHttpResult>(StrategyRunEndpoints.NoSoil());

        Assert.Equal(422, result.StatusCode);
        Assert.Equal("There is no soil data for this field's location, so it can't be modelled.", result.ProblemDetails.Title);
        Assert.Equal(FieldSoil.MissingMessage, result.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(30.5, 50.4, true)]   // Kyiv region
    [InlineData(31.5, 43.5, false)]  // Black Sea
    public void The_field_response_says_whether_the_field_has_soil(double longitude, double latitude, bool available)
    {
        var field = new Field { Name = "Test", Boundary = Square(longitude, latitude) };

        Assert.Equal(available, FieldResponse.From(field).SoilAvailable);
    }
}
