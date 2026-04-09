namespace DocIntelligence.Contracts;

public record DocumentEnrichmentFailed
{
    public Guid DocumentId { get; init; }
    public Guid BatchId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public string Error { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public DateTime FailedAt { get; init; }
}
