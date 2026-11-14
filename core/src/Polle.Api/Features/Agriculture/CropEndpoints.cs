using Polle.Api.Common;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed class CropEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Crops");
        group.MapGet("/crops", ListAsync).Produces<CropResponse[]>().WithSummary("List the crop catalog (public.crops) with each crop's growing profiles, sorted by name");
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var crops = await db.Crops.AsNoTracking().Include(crop => crop.Profiles).OrderBy(crop => crop.Name).ToListAsync(ct);
        return TypedResults.Ok(crops.Select(CropResponse.From));
    }
}
