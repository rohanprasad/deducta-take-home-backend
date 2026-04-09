using System.Net;
using System.Net.Http.Json;
using DocIntelligence.Contracts;
using MassTransit;

namespace EnrichmentService.Consumers;

public class DocumentSubmittedConsumer : IConsumer<DocumentSubmitted>
{
    private const int MaxServerErrorAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentSubmittedConsumer> _logger;

    public DocumentSubmittedConsumer(HttpClient httpClient, ILogger<DocumentSubmittedConsumer> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DocumentSubmitted> context)
    {
        var message = context.Message;
        var rateLimitAttempts = 0;
        var serverErrorAttempts = 0;

        _logger.LogInformation("Processing document {DocumentId}", message.DocumentId);

        while (!context.CancellationToken.IsCancellationRequested)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await _httpClient.PostAsJsonAsync(
                    "http://mockai:5050/api/enrich",
                    new { message.Content, message.DocumentType },
                    context.CancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rateLimitAttempts++;
                    var delay = CalculateDelay(rateLimitAttempts);

                    _logger.LogWarning(
                        "Rate limited while processing document {DocumentId}. Retrying in {DelaySeconds}s",
                        message.DocumentId,
                        delay.TotalSeconds);

                    await Task.Delay(delay, context.CancellationToken);
                    continue;
                }

                if ((int)response.StatusCode >= 500)
                {
                    serverErrorAttempts++;

                    if (serverErrorAttempts >= MaxServerErrorAttempts)
                    {
                        await PublishFailureAsync(
                            context,
                            message,
                            (int)response.StatusCode,
                            $"Mock AI returned {(int)response.StatusCode} after {serverErrorAttempts} attempts",
                            serverErrorAttempts);

                        return;
                    }

                    var delay = CalculateDelay(serverErrorAttempts);

                    _logger.LogWarning(
                        "Mock AI returned {StatusCode} for document {DocumentId}. Retrying in {DelaySeconds}s ({Attempt}/{MaxAttempts})",
                        (int)response.StatusCode,
                        message.DocumentId,
                        delay.TotalSeconds,
                        serverErrorAttempts,
                        MaxServerErrorAttempts);

                    await Task.Delay(delay, context.CancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    await PublishFailureAsync(
                        context,
                        message,
                        (int)response.StatusCode,
                        $"Mock AI returned unexpected status {(int)response.StatusCode}",
                        1);

                    return;
                }

                var result = await response.Content.ReadFromJsonAsync<EnrichmentResult>(context.CancellationToken);

                if (result is null)
                    throw new InvalidOperationException("Mock AI returned an empty enrichment response.");

                await context.Publish(new DocumentEnriched
                {
                    DocumentId = message.DocumentId,
                    BatchId = message.BatchId,
                    DocumentType = message.DocumentType,
                    Classification = result.Classification,
                    ExtractedEntities = result.Entities.ToList(),
                    ConfidenceScore = result.Confidence,
                    EnrichedAt = DateTime.UtcNow
                }, context.CancellationToken);

                return;
            }
            catch (HttpRequestException ex)
            {
                serverErrorAttempts++;

                if (serverErrorAttempts >= MaxServerErrorAttempts)
                {
                    await PublishFailureAsync(
                        context,
                        message,
                        ToNullableStatusCode(ex.StatusCode),
                        $"Mock AI request failed after {serverErrorAttempts} attempts: {ex.Message}",
                        serverErrorAttempts);

                    return;
                }

                var delay = CalculateDelay(serverErrorAttempts);

                _logger.LogWarning(
                    ex,
                    "Mock AI request failed for document {DocumentId}. Retrying in {DelaySeconds}s ({Attempt}/{MaxAttempts})",
                    message.DocumentId,
                    delay.TotalSeconds,
                    serverErrorAttempts,
                    MaxServerErrorAttempts);

                await Task.Delay(delay, context.CancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private async Task PublishFailureAsync(
        ConsumeContext<DocumentSubmitted> context,
        DocumentSubmitted message,
        int? statusCode,
        string error,
        int attemptCount)
    {
        _logger.LogError(
            "Publishing failed enrichment for document {DocumentId}. StatusCode: {StatusCode}, Attempts: {AttemptCount}",
            message.DocumentId,
            statusCode,
            attemptCount);

        await context.Publish(new DocumentEnrichmentFailed
        {
            DocumentId = message.DocumentId,
            BatchId = message.BatchId,
            DocumentType = message.DocumentType,
            StatusCode = statusCode,
            Error = error,
            AttemptCount = attemptCount,
            FailedAt = DateTime.UtcNow
        }, context.CancellationToken);
    }

    private static TimeSpan CalculateDelay(int attempt)
    {
        var exponent = Math.Max(0, attempt - 1);
        var exponentialDelayMs = BaseRetryDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedDelayMs = Math.Min(exponentialDelayMs, MaxRetryDelay.TotalMilliseconds);

        // Equal jitter: keep half the delay and randomize the other half.
        var jitteredDelayMs = (cappedDelayMs / 2) + (Random.Shared.NextDouble() * (cappedDelayMs / 2));

        return TimeSpan.FromMilliseconds(jitteredDelayMs);
    }

    private static int? ToNullableStatusCode(HttpStatusCode? statusCode)
        => statusCode.HasValue ? (int)statusCode.Value : null;
}
