namespace Polle.Domain.Agriculture;

/// <summary>
/// A season year the user opened for planning ahead, so it is listed before any crop is planned in it. Years that already
/// hold plantings are listed from the plantings themselves; this row only keeps an otherwise empty year visible.
/// </summary>
public sealed class PlanningYear
{
    public Guid UserId { get; set; }
    public int Year { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
