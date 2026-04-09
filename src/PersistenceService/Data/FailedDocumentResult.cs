namespace PersistenceService.Data;

public class FailedDocumentResult
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid BatchId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string Error { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime FailedAt { get; set; }
    public DateTime PersistedAt { get; set; }
}
