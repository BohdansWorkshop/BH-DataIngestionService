using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Domain.Entities;

namespace BH_DataIngestionService.Application.Services;

internal sealed record DuplicateTransactionKey(
    string CustomerId,
    DateTimeOffset TransactionDate,
    decimal Amount,
    string Currency,
    string SourceChannel)
{
    public static DuplicateTransactionKey FromRequest(TransactionRequest request)
    {
        return new DuplicateTransactionKey(
            request.CustomerId!.Trim(),
            request.TransactionDate,
            request.Amount,
            request.Currency!.Trim().ToUpperInvariant(),
            request.SourceChannel!.Trim());
    }

    public static DuplicateTransactionKey FromTransaction(Transaction transaction)
    {
        return new DuplicateTransactionKey(
            transaction.CustomerId,
            transaction.TransactionDate,
            transaction.Amount,
            transaction.Currency,
            transaction.SourceChannel);
    }
}
