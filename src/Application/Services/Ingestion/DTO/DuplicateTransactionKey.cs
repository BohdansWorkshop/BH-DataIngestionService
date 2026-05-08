using BH_DataIngestionService.Application.DTOs;

namespace BH_DataIngestionService.Application.Services.Ingestion.DTO;

internal readonly record struct DuplicateTransactionKey(
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
}
