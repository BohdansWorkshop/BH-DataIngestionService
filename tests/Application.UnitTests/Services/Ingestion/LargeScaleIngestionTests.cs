using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Services.Ingestion;
using BH_DataIngestionService.Application.UnitTests.TestUtilities;
using BH_DataIngestionService.Application.Validation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BH_DataIngestionService.Application.UnitTests.Services.Ingestion;

public sealed class LargeScaleIngestionTests
{
    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task IngestBatchAsync_handles_large_streamed_datasets_with_validation_and_deduplication(int validRecordCount)
    {
        await using var dbContext = TestDbContextFactory.Create();
        var service = new TransactionIngestionService(dbContext, new TransactionRequestValidator());
        await using var csvStream = new TransactionCsvStream(BuildDataset(validRecordCount));

        var result = await service.IngestBatchAsync(csvStream, CancellationToken.None);

        Assert.Equal(validRecordCount, result.AcceptedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Contains(result.Errors, error => error.ErrorCode == "DUPLICATE_TRANSACTION");
        Assert.Contains(result.Errors, error => error.ErrorCode == "VALIDATION_ERROR");
        Assert.Equal(validRecordCount, await dbContext.Transactions.CountAsync());
    }

    private static IEnumerable<TransactionRequest> BuildDataset(int validRecordCount)
    {
        var fixedTransaction = new TransactionRequest(
            "CUST-FIXED-0001",
            DateTimeOffset.UtcNow.AddDays(-10),
            123.45m,
            "USD",
            "web");

        yield return fixedTransaction;

        var faker = new TransactionRequestFaker();
        foreach (var transaction in faker.GenerateUnique(validRecordCount - 1))
        {
            yield return transaction;
        }

        yield return fixedTransaction;

        yield return new TransactionRequest(
            "",
            DateTimeOffset.UtcNow.AddDays(1),
            -10,
            "US1",
            "");
    }
}
