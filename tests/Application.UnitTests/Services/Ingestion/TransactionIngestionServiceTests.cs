using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Exceptions;
using BH_DataIngestionService.Application.Services.Ingestion;
using BH_DataIngestionService.Application.UnitTests.TestUtilities;
using BH_DataIngestionService.Application.Validation;
using Xunit;

namespace BH_DataIngestionService.Application.UnitTests.Services.Ingestion;

public sealed class TransactionIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_rejects_duplicate_transaction()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var service = new TransactionIngestionService(dbContext, new TransactionRequestValidator());
        var request = new TransactionRequest(
            "customer-1",
            DateTimeOffset.UtcNow.AddDays(-1),
            25.50m,
            "usd",
            "web");

        await service.IngestAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateTransactionException>(() =>
            service.IngestAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task IngestBatchAsync_streams_rows_and_reports_invalid_rows()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var service = new TransactionIngestionService(dbContext, new TransactionRequestValidator());
        const string csv = """
                           CustomerId,TransactionDate,Amount,Currency,SourceChannel
                           C1,2024-01-01T00:00:00Z,10.00,USD,web
                           ,2024-01-01T00:00:00Z,-5.00,USD,web
                           C1,2024-01-01T00:00:00Z,10.00,USD,web
                           """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var result = await service.IngestBatchAsync(stream, CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Contains(result.Errors, error => error.ErrorCode == "VALIDATION_ERROR");
        Assert.Contains(result.Errors, error => error.ErrorCode == "DUPLICATE_TRANSACTION");
    }
}
