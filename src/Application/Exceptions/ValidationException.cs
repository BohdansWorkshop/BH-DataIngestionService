namespace BH_DataIngestionService.Application.Exceptions;

public sealed class ValidationException : Exception
{
    public ValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
