using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BH_DataIngestionService.Application.Services.Transactions;

public sealed class TransactionQueryService(ApplicationDbContext dbContext)
{
    public async Task<PagedResponse<TransactionResponse>> GetCustomerTransactionsAsync(
        string customerId,
        int page,
        int pageSize,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        string? currency,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var transactionsQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId);

        if (dateFrom.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(transaction => transaction.TransactionDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(transaction => transaction.TransactionDate <= dateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            var normalizedCurrency = currency.Trim().ToUpperInvariant();
            transactionsQuery = transactionsQuery.Where(transaction => transaction.Currency == normalizedCurrency);
        }

        var totalCount = await transactionsQuery.LongCountAsync(cancellationToken);

        var items = await transactionsQuery
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(transaction => new TransactionResponse(
                transaction.Id,
                transaction.CustomerId,
                transaction.TransactionDate,
                transaction.Amount,
                transaction.Currency,
                transaction.SourceChannel,
                transaction.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<TransactionResponse>(items, page, pageSize, totalCount);
    }
}
