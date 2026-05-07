using BH_DataIngestionService.Application.DTOs;
using FluentValidation;

namespace BH_DataIngestionService.Application.Validation;

public sealed class TransactionRequestValidator : AbstractValidator<TransactionRequest>
{
    public TransactionRequestValidator()
    {
        RuleFor(request => request.CustomerId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(request => request.TransactionDate)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow)
            .WithMessage("TransactionDate must not be in the future.");

        RuleFor(request => request.Amount)
            .GreaterThan(0);

        RuleFor(request => request.Currency)
            .NotEmpty()
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a 3-letter ISO currency code.");

        RuleFor(request => request.SourceChannel)
            .NotEmpty()
            .MaximumLength(50);
    }
}
