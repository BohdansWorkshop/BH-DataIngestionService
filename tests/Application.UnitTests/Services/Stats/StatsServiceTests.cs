using BH_DataIngestionService.Application.Services;
using BH_DataIngestionService.Application.Services.Stats;
using BH_DataIngestionService.Application.UnitTests.TestUtilities;
using BH_DataIngestionService.Domain.Entities;
using Xunit;

namespace BH_DataIngestionService.Application.UnitTests.Services.Stats;

public sealed class StatsServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_returns_expected_aggregates()
    {
        await using var dbContext = TestDbContextFactory.Create();
        dbContext.Transactions.AddRange(
            CreateTransaction("C1", 10, "USD", "web"),
            CreateTransaction("C1", 15, "USD", "mobile"),
            CreateTransaction("C2", 20, "EUR", "web"));
        await dbContext.SaveChangesAsync();

        var service = new StatsService(dbContext);

        var result = await service.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(3, result.TotalTransactions);
        Assert.Contains(result.TotalAmountByCurrency, item => item.Currency == "USD" && item.TotalAmount == 25);
        Assert.Contains(result.TotalAmountByCurrency, item => item.Currency == "EUR" && item.TotalAmount == 20);
        Assert.Equal("C1", result.TopCustomersByTransactionCount[0].CustomerId);
        Assert.Contains(result.TransactionsBySourceChannel, item => item.SourceChannel == "web" && item.TransactionCount == 2);
    }

    private static Transaction CreateTransaction(string customerId, decimal amount, string currency, string sourceChannel)
    {
        return new Transaction
        {
            CustomerId = customerId,
            TransactionDate = DateTimeOffset.UtcNow.AddDays(-1),
            Amount = amount,
            Currency = currency,
            SourceChannel = sourceChannel,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
