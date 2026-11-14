namespace Polle.Domain.Agriculture;

/// <summary>
/// Read-only crop species from the solver's catalog (public.crops), maintained by the data team in Expert data.
/// The application never writes it; plantings reference one of its <see cref="Profiles"/>.
/// </summary>
public sealed class Crop
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public string? NameUk { get; init; }
    public string? ScientificName { get; init; }
    public List<CropProfile> Profiles { get; init; } = [];
}
