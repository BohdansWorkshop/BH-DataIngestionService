using BH_DataIngestionService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BH_DataIngestionService.Web.Controllers;

[ApiController]
[Route("customers")]
public sealed class CustomersController(TransactionQueryService queryService) : ControllerBase
{
    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(
        string id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTimeOffset? dateFrom = null,
        [FromQuery] DateTimeOffset? dateTo = null,
        [FromQuery] string? currency = null,
        CancellationToken cancellationToken = default)
    {
        var result = await queryService.GetCustomerTransactionsAsync(
            id,
            page,
            pageSize,
            dateFrom,
            dateTo,
            currency,
            cancellationToken);

        return Ok(result);
    }
}
