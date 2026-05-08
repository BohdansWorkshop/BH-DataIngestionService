using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Services.Ingestion.DTO;

namespace BH_DataIngestionService.Application.Services.Ingestion;

public class BatchIngestionState
{
    public int AcceptedCount { get; set; }

    public List<PendingTransactionRow> PendingRows { get; } = new(); 

    public List<RowError> Errors { get; } = [];

    public BatchIngestResponse ToResponse()
    {
        return new BatchIngestResponse(AcceptedCount, Errors.Count, Errors);
    }
}
