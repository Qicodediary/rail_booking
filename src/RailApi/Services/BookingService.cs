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

        var service = await db.TrainServices
            .Include(s => s.Origin)
            .Include(s => s.Destination)
            .FirstOrDefaultAsync(s => s.ServiceCode == request.ServiceCode, ct)
            ?? throw new BookingException($"Unknown service '{request.ServiceCode}'.");

        if (service.Status == ServiceStatus.Cancelled)
            throw new BookingException("This service has been cancelled.");

        var departureAt = request.TravelDate.ToDateTime(service.DepartureTime);
        var now = clock.GetUtcNow().DateTime;

        if (departureAt < now)
            throw new BookingException("This service has already departed.");

        // Serialisable transaction so two concurrent bookings cannot oversell the last seats.
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var seatsTaken = await db.Bookings
            .Where(b => b.TrainServiceId == service.Id && b.TravelDate == request.TravelDate)
            .SumAsync(b => (int?)b.PassengerCount, ct) ?? 0;

        var seatsRemaining = service.TotalSeats - seatsTaken;
        if (seatsRemaining < request.PassengerCount)
            throw new BookingException($"Only {seatsRemaining} seat(s) left on this service.");

        var fare = fares.Calculate(
            service.BaseFare, departureAt, now, request.PassengerCount, request.HasRailcard);

        var booking = new Booking
        {
            Reference = await GenerateUniqueReferenceAsync(ct),
            TrainServiceId = service.Id,
            TravelDate = request.TravelDate,
            PassengerName = request.PassengerName,
            PassengerCount = request.PassengerCount,
            HasRailcard = request.HasRailcard,
            TotalPrice = fare.Total,
            CreatedAt = clock.GetUtcNow()
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
            .AsNoTracking()
            .Include(b => b.TrainService).ThenInclude(s => s.Origin)
            .Include(b => b.TrainService).ThenInclude(s => s.Destination)
            .FirstOrDefaultAsync(b => b.Reference == reference.ToUpperInvariant(), ct);

        return booking is null ? null : ToDto(booking, booking.TrainService);
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
        CreatedAt: b.CreatedAt);

    private async Task<string> GenerateUniqueReferenceAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var reference = "TL" + RandomNumberGenerator.GetString(ReferenceAlphabet, 6);
            if (!await db.Bookings.AnyAsync(b => b.Reference == reference, ct))
                return reference;
        }

        throw new InvalidOperationException("Could not allocate a unique booking reference.");
    }
}
