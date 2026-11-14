namespace Polle.Domain.Agriculture;

/// <summary>A strategy the farmer kept for a field, with the farmer's own plan as scored in the same run for comparison.</summary>
public sealed class FieldStrategy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FieldId { get; set; }
    public Field Field { get; set; } = null!;
    public required string Key { get; set; }
    /// <summary>Copied from the run's job: the farmer's own name and description of a custom strategy, else null.</summary>
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public required string StartMonth { get; set; }
    public required string EndMonth { get; set; }
    public required string ResultJson { get; set; }
    public string? BaselineJson { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}
