using BH_DataIngestionService.Application.Services.Stats;
using Microsoft.AspNetCore.Mvc;

namespace BH_DataIngestionService.Web.Controllers;

[ApiController]
[Route("stats")]
public sealed class StatsController(StatsService statsService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        return Ok(await statsService.GetSummaryAsync(cancellationToken));
    }
}
