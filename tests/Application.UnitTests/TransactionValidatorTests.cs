using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Validation;
using Xunit;

namespace BH_DataIngestionService.Application.UnitTests;

public sealed class TransactionValidatorTests
{
    [Fact]
    public void Validate_returns_errors_for_required_fields_and_invalid_amount()
    {
        var validator = new TransactionValidator();
        var request = new TransactionRequest(
            "",
            DateTimeOffset.UtcNow,
            0,
            null,
            " ");

        var errors = validator.Validate(request);

        Assert.Contains(nameof(TransactionRequest.CustomerId), errors.Keys);
        Assert.Contains(nameof(TransactionRequest.Amount), errors.Keys);
        Assert.Contains(nameof(TransactionRequest.Currency), errors.Keys);
        Assert.Contains(nameof(TransactionRequest.SourceChannel), errors.Keys);
    }

    [Fact]
    public void Validate_rejects_dates_far_in_future()
    {
        var validator = new TransactionValidator();
        var request = new TransactionRequest(
            "customer-1",
            DateTimeOffset.UtcNow.AddDays(1),
            10,
            "USD",
            "web");

        var errors = validator.Validate(request);

        Assert.Contains(nameof(TransactionRequest.TransactionDate), errors.Keys);
    }
}
