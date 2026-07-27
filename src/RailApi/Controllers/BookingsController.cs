using Microsoft.AspNetCore.Mvc;
using RailApi.Dtos;
using RailApi.Services;

namespace RailApi.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController(IBookingService bookings) : ControllerBase
{
    /// <summary>Books seats on a service and returns the booking reference.</summary>
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BookingDto>> Create(
        [FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        try
        {
            var booking = await bookings.CreateAsync(request, ct);
            return CreatedAtAction(
                nameof(GetByReference),
                new { reference = booking.Reference },
                booking);
        }
        catch (BookingException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Booking rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>Retrieves a booking by its reference.</summary>
    [HttpGet("{reference}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingDto>> GetByReference(string reference, CancellationToken ct)
    {
        var booking = await bookings.GetByReferenceAsync(reference, ct);
        return booking is null ? NotFound() : Ok(booking);
    }
    /// <summary> cancels a booking by its reference. </summary>
    [HttpPost("{reference}/cancel")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
    public async Task<ActionResult<BookingDto>> Cancel(String reference, CancellationToken ct)
    {
        try
        {
            var booking = await bookings.CancelAsync(reference, ct);
            return Ok(booking);
        }
        catch (BookingException ex)
        {
            return BadRequest( new ProblemDetails
            {
                Title ="Cancellation rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }


    
}
