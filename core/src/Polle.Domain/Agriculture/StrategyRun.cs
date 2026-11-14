namespace Polle.Domain.Agriculture;

/// <summary>One "Suggest strategies" request for a field: the solver tasks it submitted and what came back.</summary>
public sealed class StrategyRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FieldId { get; set; }
    public Field Field { get; set; } = null!;
    public required string StartMonth { get; set; } // YYYY-MM, inclusive
    public required string EndMonth { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<StrategyJob> Jobs { get; set; } = [];
}

/// <summary>One solver task of a run: a strategy optimisation or the evaluation of the farmer's own plan.</summary>
public sealed class StrategyJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public required string Key { get; set; }
    /// <summary>The farmer's own name and description for a custom strategy; null shows the catalog's wording for the key.</summary>
    public string? Name { get; set; }
    public string? Summary { get; set; }
    public required string Kind { get; set; } // optimization | evaluation
    public string? TaskId { get; set; }
    public required string State { get; set; } // processing | ready | failed
    public string? ResultJson { get; set; } // felder OptimizeFieldResponse, stored as jsonb
    public string? Error { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow; // last (re)submission to the solver
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
