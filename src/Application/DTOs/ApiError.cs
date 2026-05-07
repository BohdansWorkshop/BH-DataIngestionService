namespace BH_DataIngestionService.Application.DTOs;

public sealed record ApiError(
    string ErrorCode,
    string Message,
    IReadOnlyDictionary<string, string[]>? Details = null);
