using Microsoft.AspNetCore.Mvc;
using RailApi.Dtos;
using RailApi.Services;

namespace RailApi.Controllers;

[ApiController]
[Route("api/journeys")]
public class JourneysController(IJourneySearchService search) : ControllerBase
{
    /// <summary>Searches services between two stations on a given date, with fares.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<JourneyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<JourneyDto>>> Search(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] DateOnly date,
        [FromQuery] bool railcard = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest("Both 'from' and 'to' are required.");

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Origin and destination must differ.");

        var results = await search.SearchAsync(from, to, date, railcard, ct);
        return Ok(results);
    }
}
