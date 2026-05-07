using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BH_DataIngestionService.Application.Services.Stats;

public sealed class StatsService(ApplicationDbContext dbContext)
{
    public async Task<StatsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var totalTransactions = await dbContext.Transactions
            .AsNoTracking()
            .LongCountAsync(cancellationToken);

        var amountByCurrencyRows = await dbContext.Transactions
            .AsNoTracking()
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                TotalAmount = group.Sum(transaction => transaction.Amount)
            })
            .OrderBy(item => item.Currency)
            .ToListAsync(cancellationToken);

        var topCustomerRows = await dbContext.Transactions
            .AsNoTracking()
            .GroupBy(transaction => transaction.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                TransactionCount = group.Count()
            })
            .OrderByDescending(item => item.TransactionCount)
            .ThenBy(item => item.CustomerId)
            .Take(10)
            .ToListAsync(cancellationToken);

        var sourceChannelRows = await dbContext.Transactions
            .AsNoTracking()
            .GroupBy(transaction => transaction.SourceChannel)
            .Select(group => new
            {
                SourceChannel = group.Key,
                TransactionCount = group.Count()
            })
            .OrderByDescending(item => item.TransactionCount)
            .ThenBy(item => item.SourceChannel)
            .ToListAsync(cancellationToken);

        return new StatsSummaryResponse(
            totalTransactions,
            amountByCurrencyRows
                .Select(item => new AmountByCurrencyDto(item.Currency, item.TotalAmount))
                .ToList(),
            topCustomerRows
                .Select(item => new CustomerTransactionCountDto(item.CustomerId, item.TransactionCount))
                .ToList(),
            sourceChannelRows
                .Select(item => new SourceChannelCountDto(item.SourceChannel, item.TransactionCount))
                .ToList());
    }
}
