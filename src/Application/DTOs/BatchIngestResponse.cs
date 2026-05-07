namespace BH_DataIngestionService.Application.DTOs;

public sealed record BatchIngestResponse(
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<RowError> Errors);

public sealed record RowError(
    int RowNumber,
    string ErrorCode,
    string Message,
    IReadOnlyDictionary<string, string[]>? Details = null);
