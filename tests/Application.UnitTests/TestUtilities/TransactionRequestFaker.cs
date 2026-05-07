using BH_DataIngestionService.Application.DTOs;
using Bogus;

namespace BH_DataIngestionService.Application.UnitTests.TestUtilities;

internal sealed class TransactionRequestFaker
{
    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "CAD", "AUD"];
    private static readonly string[] SourceChannels = ["web", "mobile", "api"];
    private readonly Faker<TransactionRequest> faker;

    public TransactionRequestFaker()
    {
        faker = new Faker<TransactionRequest>()
            .CustomInstantiator(fake => new TransactionRequest(
                $"CUST-{fake.Random.AlphaNumeric(10).ToUpperInvariant()}",
                fake.Date.RecentOffset(90, DateTimeOffset.UtcNow.AddDays(-1)),
                Math.Round(fake.Finance.Amount(1, 5000), 2),
                fake.PickRandom(Currencies),
                fake.PickRandom(SourceChannels)));
    }

    public TransactionRequest Generate()
    {
        return faker.Generate();
    }

    public IEnumerable<TransactionRequest> GenerateUnique(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var transaction = Generate();
            yield return transaction with
            {
                CustomerId = $"CUST-{index:D8}",
                TransactionDate = DateTimeOffset.UtcNow.AddDays(-90).AddSeconds(index),
                Amount = Math.Round(transaction.Amount + index % 100 / 100m, 2)
            };
        }
    }
}
