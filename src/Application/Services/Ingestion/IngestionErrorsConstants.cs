namespace BH_DataIngestionService.Application.Services.Ingestion;

public static class IngestionErrorsConstants
{
    public const string CsvParseErrorTopic = "CSV_PARSE_ERROR";
    public const string ValidationErrorTopic = "VALIDATION_ERROR";
    public const string DuplicateTransactionTopic = "DUPLICATE_TRANSACTION";
    
    public const string InvalidTransactionMessage = "Invalid transaction.";
    public const string CsvParseErrorMessage = "CSV row could not be parsed.";
    public const string DuplicateInBatchMessage = "Duplicate transaction in batch.";
    public const string DuplicateInCsvMessage = "Duplicate transaction in uploaded CSV.";
    public const string DuplicateTransactionMessage = "Transaction already exists.";
}
