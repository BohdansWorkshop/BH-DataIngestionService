using BH_DataIngestionService.Application.DTOs;

namespace BH_DataIngestionService.Application.Services.Ingestion.DTO;

public readonly record struct PendingTransactionRow(int RowNumber, TransactionRequest Request);
