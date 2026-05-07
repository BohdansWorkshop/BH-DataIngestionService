namespace BH_DataIngestionService.Application.DTOs;

public sealed record StatsSummaryResponse(
    long TotalTransactions,
    IReadOnlyList<AmountByCurrencyDto> TotalAmountByCurrency,
    IReadOnlyList<CustomerTransactionCountDto> TopCustomersByTransactionCount,
    IReadOnlyList<SourceChannelCountDto> TransactionsBySourceChannel);

public sealed record AmountByCurrencyDto(string Currency, decimal TotalAmount);

public sealed record CustomerTransactionCountDto(string CustomerId, int TransactionCount);

public sealed record SourceChannelCountDto(string SourceChannel, int TransactionCount);
