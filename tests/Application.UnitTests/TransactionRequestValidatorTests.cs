using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Validation;
using Xunit;

namespace BH_DataIngestionService.Application.UnitTests;

public sealed class TransactionRequestValidatorTests
{
    [Fact]
    public void Validate_returns_errors_for_required_fields_and_invalid_amount()
    {
        var validator = new TransactionRequestValidator();
        var request = new TransactionRequest(
            "",
            DateTimeOffset.UtcNow,
            0,
            null,
            " ");

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.CustomerId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.Amount));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.Currency));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.SourceChannel));
    }

    [Fact]
    public void Validate_rejects_future_transaction_date()
    {
        var validator = new TransactionRequestValidator();
        var request = new TransactionRequest(
            "customer-1",
            DateTimeOffset.UtcNow.AddMinutes(1),
            10,
            "USD",
            "web");

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.TransactionDate));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("USDD")]
    public void Validate_rejects_invalid_currency_codes(string currency)
    {
        var validator = new TransactionRequestValidator();
        var request = new TransactionRequest(
            "customer-1",
            DateTimeOffset.UtcNow.AddDays(-1),
            10,
            currency,
            "web");

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TransactionRequest.Currency));
    }
}
