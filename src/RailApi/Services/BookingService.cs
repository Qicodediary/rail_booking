using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RailApi.Data;
using RailApi.Dtos;
using RailApi.Models;

namespace RailApi.Services;

public interface IBookingService
{
    Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default);
    Task<BookingDto?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<BookingDto> CancelAsync(string reference, CancellationToken ct = default);
    Task<PagedResult<BookingDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Thrown for rule violations the caller can fix — maps to a 4xx, not a 500.</summary>
public class BookingException(string message) : Exception(message);

public class BookingService(
    RailDbContext db,
    IFareCalculator fares,
    ILogger<BookingService> logger,
    TimeProvider clock) : IBookingService
{
    private const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        if (request.PassengerCount < 1)
            throw new BookingException("At least one passenger is required.");

        var service = await db.TrainServices // check database can take a long time, so make sure use await to avoid blocking the thread
            .Include(s => s.Origin) //Id is number so need to obtain the orign station's all details
            .Include(s => s.Destination)
            .FirstOrDefaultAsync(s => s.ServiceCode == request.ServiceCode, ct) // return the first service whose ServiceCode matches the request, if not return null
            ?? throw new BookingException($"Unknown service '{request.ServiceCode}'."); // ?? : if the query found a service, use it; if null, throw an exception (booking stops here)

        if (service.Status == ServiceStatus.Cancelled)
            throw new BookingException("This service has been cancelled.");

        var departureAt = request.TravelDate.ToDateTime(service.DepartureTime);
        var now = clock.GetUtcNow().DateTime;

        if (departureAt < now)
            throw new BookingException("This service has already departed.");

        // Serialisable transaction so two concurrent bookings cannot oversell the last seats.
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct); // using ensures the transection is disposed properly whether the code succeds or throws, it commits or rolls bank and releases the lock qutomatically

        var seatsTaken = await db.Bookings
            .Where(b => b.TrainServiceId == service.Id && b.TravelDate == request.TravelDate)
            .SumAsync(b => (int?)b.PassengerCount, ct) ?? 0;

        var seatsRemaining = service.TotalSeats - seatsTaken;
        if (seatsRemaining < request.PassengerCount)
            throw new BookingException($"Only {seatsRemaining} seat(s) left on this service.");

        var fare = fares.Calculate(
            service.BaseFare, departureAt, now, request.PassengerCount, request.HasRailcard, request.InfantCount, request.SeatClass);

        var booking = new Booking
        {
            Reference = await GenerateUniqueReferenceAsync(ct),
            TrainServiceId = service.Id,
            TravelDate = request.TravelDate,
            PassengerName = request.PassengerName,
            PassengerCount = request.PassengerCount,
            HasRailcard = request.HasRailcard,
            TotalPrice = fare.Total,
            CreatedAt = clock.GetUtcNow(),
            SeatClass = request.SeatClass,
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Created booking {Reference} for {ServiceCode} on {TravelDate} ({Passengers} passengers, {Total})",
            booking.Reference, service.ServiceCode, request.TravelDate, request.PassengerCount, fare.Total);

        return ToDto(booking, service);
    }

    public async Task<BookingDto?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var booking = await db.Bookings
            .AsNoTracking() // read only, no changes
            .Include(b => b.TrainService).ThenInclude(s => s.Origin)
            .Include(b => b.TrainService).ThenInclude(s => s.Destination)
            .FirstOrDefaultAsync(b => b.Reference == reference.ToUpperInvariant(), ct);

        return booking is null ? null : ToDto(booking, booking.TrainService);
    }

    public async Task<BookingDto> CancelAsync(string reference, CancellationToken ct = default)
    { 
        // 1. find the booking by reference
        var booking = await db.Bookings
            .Include(b => b.TrainService).ThenInclude(s => s.Origin)
            .Include(b => b.TrainService).ThenInclude(s => s.Destination)
            .FirstOrDefaultAsync(b => b.Reference == reference.ToUpperInvariant(), ct)
            ?? throw new BookingException($"Booking '{reference}' not found.");

        // 2. find the train, and get the departure time
        var departureAt = booking.TravelDate.ToDateTime(booking.TrainService.DepartureTime);
        var now = clock.GetUtcNow().DateTime;

        // 3. check if this booking can be cancelled 
        if (departureAt < now)
            throw new BookingException("This service has already departed, so the booking cannot be cancelled.");

        var TUntilDeparture = departureAt - now;
        decimal refund;
        if (TUntilDeparture.TotalHours > 24)
            refund = booking.TotalPrice;
        else
            refund = 0m;


        // 4. update the booking status 
        booking.Status = BookingStatus.Cancelled;
        booking.RefoundAmount = refund;
        await db.SaveChangesAsync(ct); // save to database
        return ToDto(booking, booking.TrainService);
    }

    private static BookingDto ToDto(Booking b, TrainService s) => new(
        Reference: b.Reference,
        ServiceCode: s.ServiceCode,
        OriginCrs: s.Origin.Crs,
        DestinationCrs: s.Destination.Crs,
        TravelDate: b.TravelDate,
        DepartureAt: b.TravelDate.ToDateTime(s.DepartureTime),
        PassengerName: b.PassengerName,
        PassengerCount: b.PassengerCount,
        TotalPrice: b.TotalPrice,
        CreatedAt: b.CreatedAt,
        Status: b.Status,
        RefundAmount: b.RefoundAmount,
        SeatClass: b.SeatClass);

    private async Task<string> GenerateUniqueReferenceAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var reference = "TL" + RandomNumberGenerator.GetString(ReferenceAlphabet, 6);
            if (await db.Bookings.AllAsync(b => b.Reference != reference, ct))
                return reference;
        }

        throw new InvalidOperationException("Could not allocate a unique booking reference.");
    }
    public async Task<PagedResult<BookingDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var totalCount = await db.Bookings.CountAsync(ct);
        var bookings = await db.Bookings
            .AsNoTracking()
            .Include(b => b.TrainService).ThenInclude(s => s.Origin)
            .Include(b => b.TrainService).ThenInclude(s => s.Destination)
            .OrderByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = bookings.Select(b => ToDto(b, b.TrainService)).ToList();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new PagedResult<BookingDto>(items, page, pageSize, totalCount, totalPages);
  
    }
}
