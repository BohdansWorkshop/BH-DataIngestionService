namespace BH_DataIngestionService.Application.DTOs;

public sealed record TransactionRequest(
    string CustomerId,
    DateTimeOffset TransactionDate,
    decimal Amount,
    string? Currency,
    string? SourceChannel);

public sealed record TransactionResponse(
    Guid Id,
    string CustomerId,
    DateTimeOffset TransactionDate,
    decimal Amount,
    string Currency,
    string SourceChannel,
    DateTimeOffset CreatedAtUtc);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount);
