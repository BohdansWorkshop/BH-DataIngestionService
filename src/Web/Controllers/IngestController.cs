using BH_DataIngestionService.Application.DTOs;
using BH_DataIngestionService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BH_DataIngestionService.Web.Controllers;

[ApiController]
[Route("ingest")]
public sealed class IngestController(TransactionIngestionService ingestionService) : ControllerBase
{
    [HttpPost("transaction")]
    public async Task<IActionResult> IngestTransaction(
        [FromBody] TransactionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await ingestionService.IngestAsync(request, cancellationToken);
        return Created($"/customers/{response.CustomerId}/transactions", response);
    }

    [HttpPost("batch")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> IngestBatch([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiError("VALIDATION_ERROR", "CSV file is required."));
        }

        await using var stream = file.OpenReadStream();
        var response = await ingestionService.IngestBatchAsync(stream, cancellationToken);
        return Ok(response);
    }
}
