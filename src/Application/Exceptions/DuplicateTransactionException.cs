namespace BH_DataIngestionService.Application.Exceptions;

public sealed class DuplicateTransactionException : Exception
{
    public DuplicateTransactionException()
        : base("A transaction with the same customer, date, amount, currency and source channel already exists.")
    {
    }
}
