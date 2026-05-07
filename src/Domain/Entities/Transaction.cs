namespace BH_DataIngestionService.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string CustomerId { get; set; }

    public DateTimeOffset TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required string SourceChannel { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
