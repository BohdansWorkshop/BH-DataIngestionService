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
    public static DuplicateTransactionKey FromRequest(TransactionRequest normalizedRequest)
    {
        return new DuplicateTransactionKey(
            normalizedRequest.CustomerId!,
            normalizedRequest.TransactionDate,
            normalizedRequest.Amount,
            normalizedRequest.Currency!,
            normalizedRequest.SourceChannel!);
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
