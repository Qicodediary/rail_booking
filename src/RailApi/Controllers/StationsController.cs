using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RailApi.Data;
using RailApi.Dtos;

namespace RailApi.Controllers;

[ApiController]
[Route("api/stations")]
public class StationsController(RailDbContext db) : ControllerBase
{
    /// <summary>Lists stations, optionally filtered by a name or CRS fragment.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<StationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StationDto>>> GetAll(
        [FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Stations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(s => s.Crs == term || s.Name.ToUpper().Contains(term));
        }

        var stations = await query
            .OrderBy(s => s.Name)
            .Select(s => new StationDto(s.Crs, s.Name, s.City))
            .ToListAsync(ct);

        return Ok(stations);
    }
}
