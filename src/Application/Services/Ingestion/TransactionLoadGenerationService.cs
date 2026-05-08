using System.Diagnostics;
using BH_DataIngestionService.Application.Services.Ingestion.DTO;

namespace BH_DataIngestionService.Application.Services.Ingestion;

public sealed class TransactionLoadGenerationService(
    TransactionIngestionService ingestionService,
    TransactionTestDataGenerator testDataGenerator)
{
    public async Task<GenerateLoadResponse> GenerateLoadAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requests = testDataGenerator.GenerateTransactions();
        var response = await ingestionService.IngestBatchAsync(requests, cancellationToken);

        stopwatch.Stop();

        return new GenerateLoadResponse(
            TransactionTestDataGenerator.DefaultLoadGenerationCount,
            response.AcceptedCount,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
