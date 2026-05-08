namespace BH_DataIngestionService.Application.Services.Ingestion.DTO;

internal sealed class BatchTransactionRow
{
    public required string CustomerId { get; set; }

    public DateTimeOffset TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public string? Currency { get; set; }

    public string? SourceChannel { get; set; }
}
