namespace Polle.Domain.Catalog;

/// <summary>
/// One edit made on the expert catalog page: which solver reference row changed, by whom, and the row before and after
/// (JSON). Rows removed by ON DELETE CASCADE are summarised as table counts, not stored individually.
/// </summary>
public sealed class CatalogChange
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public required string TableName { get; set; }
    /// <summary>insert, update or delete.</summary>
    public required string Action { get; set; }
    public required string RowKey { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    /// <summary>For deletes: rows removed from other tables by cascading foreign keys, as {"table": count}.</summary>
    public string? Cascade { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
