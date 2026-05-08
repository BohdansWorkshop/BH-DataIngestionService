using BH_DataIngestionService.Application.DTOs;

namespace BH_DataIngestionService.Application.Services.Ingestion;

public sealed class TransactionTestDataGenerator
{
    public const int DefaultLoadGenerationCount = 100_000;

    private static readonly string[] Currencies = ["USD", "EUR", "UAH"];
    private static readonly string[] SourceChannels = ["WEB", "MOBILE", "API"];

    public IEnumerable<TransactionRequest> GenerateTransactions(int count = DefaultLoadGenerationCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        for (var index = 1; index <= count; index++)
        {
            yield return GenerateTransactionRequest(index);
        }
    }

    private TransactionRequest GenerateTransactionRequest(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);

        var transactionDate = DateTimeOffset.UtcNow.AddSeconds(-Random.Shared.Next(0, 30 * 24 * 60 * 60));
        var amount = Random.Shared.Next(100, 100_001) / 100m;

        return new TransactionRequest(
            $"TEST-CUST-{index:D6}",
            transactionDate,
            amount,
            Currencies[Random.Shared.Next(Currencies.Length)],
            SourceChannels[Random.Shared.Next(SourceChannels.Length)]);
    }
}
