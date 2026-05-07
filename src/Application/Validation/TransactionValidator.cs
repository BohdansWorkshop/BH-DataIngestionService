using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Exceptions;

namespace BH_DataIngestionService.Application.Validation;

public sealed class TransactionValidator
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public IReadOnlyDictionary<string, string[]> Validate(TransactionRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddRequiredError(errors, nameof(request.CustomerId), request.CustomerId);
        AddRequiredError(errors, nameof(request.Currency), request.Currency);
        AddRequiredError(errors, nameof(request.SourceChannel), request.SourceChannel);

        if (request.Amount <= 0)
        {
            AddError(errors, nameof(request.Amount), "Amount must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(request.Currency) && request.Currency.Trim().Length != 3)
        {
            AddError(errors, nameof(request.Currency), "Currency must be a 3-letter ISO currency code.");
        }

        if (request.TransactionDate > DateTimeOffset.UtcNow.Add(FutureTolerance))
        {
            AddError(errors, nameof(request.TransactionDate), "TransactionDate cannot be far in the future.");
        }

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    public void ThrowIfInvalid(TransactionRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException("Invalid transaction.", errors);
        }
    }

    private static void AddRequiredError(Dictionary<string, List<string>> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, $"{field} is required.");
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, string field, string error)
    {
        if (!errors.TryGetValue(field, out var fieldErrors))
        {
            fieldErrors = [];
            errors[field] = fieldErrors;
        }

        fieldErrors.Add(error);
    }
}
