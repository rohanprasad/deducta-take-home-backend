using DocIntelligence.Contracts;
using MassTransit;
using PersistenceService.Data;

namespace PersistenceService.Consumers;

public class DocumentEnrichmentFailedConsumer : IConsumer<DocumentEnrichmentFailed>
{
    private readonly AppDbContext _db;
    private readonly ILogger<DocumentEnrichmentFailedConsumer> _logger;

    public DocumentEnrichmentFailedConsumer(
        AppDbContext db,
        ILogger<DocumentEnrichmentFailedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DocumentEnrichmentFailed> context)
    {
        var message = context.Message;

        var failedResult = new FailedDocumentResult
        {
            Id = Guid.NewGuid(),
            DocumentId = message.DocumentId,
            BatchId = message.BatchId,
            DocumentType = message.DocumentType,
            StatusCode = message.StatusCode,
            Error = message.Error,
            AttemptCount = message.AttemptCount,
            FailedAt = message.FailedAt,
            PersistedAt = DateTime.UtcNow
        };

        _db.FailedDocumentResults.Add(failedResult);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Persisted failed document {DocumentId}", message.DocumentId);
    }
}
