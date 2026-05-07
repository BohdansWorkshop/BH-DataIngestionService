using FluentValidation.Results;

namespace BH_DataIngestionService.Application.Validation;

internal static class ValidationResultExtensions
{
    public static IReadOnlyDictionary<string, string[]> ToErrorDictionary(this ValidationResult result)
    {
        return result.Errors
            .GroupBy(error => error.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }
}
