using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RailApi.Data;
using RailApi.Dtos;
using RailApi.Models;
using StackExchange.Redis;

namespace RailApi.Services;

public interface IJourneySearchService
{
    Task<IReadOnlyList<JourneyDto>> SearchAsync(
        string fromCrs, string toCrs, DateOnly date, bool hasRailcard, CancellationToken ct = default);
}

public class JourneySearchService(
    RailDbContext db,
    IFareCalculator fares,
    IConnectionMultiplexer? redis,   // nullable so tests can pass null; DI always supplies one
    ILogger<JourneySearchService> logger,
    TimeProvider clock) : IJourneySearchService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<JourneyDto>> SearchAsync(
        string fromCrs, string toCrs, DateOnly date, bool hasRailcard, CancellationToken ct = default)
    {
        fromCrs = fromCrs.ToUpperInvariant();
        toCrs = toCrs.ToUpperInvariant();

        var cacheKey = $"journeys:{fromCrs}:{toCrs}:{date:yyyy-MM-dd}:{hasRailcard}";

        var cached = await TryReadCacheAsync(cacheKey);
        if (cached is not null)
        {
            logger.LogInformation("Journey search cache hit for {CacheKey}", cacheKey);
            return cached;
        }

        var now = clock.GetUtcNow().DateTime;

        var services = await db.TrainServices
            .AsNoTracking()
            .Include(s => s.Origin)
            .Include(s => s.Destination)
            .Where(s => s.Origin.Crs == fromCrs && s.Destination.Crs == toCrs)
            .OrderBy(s => s.DepartureTime)
            .ToListAsync(ct);

        var serviceIds = services.Select(s => s.Id).ToList();

        // One grouped query for seat counts rather than one per service (avoids N+1).
        var booked = await db.Bookings
            .AsNoTracking()
            .Where(b => serviceIds.Contains(b.TrainServiceId) && b.TravelDate == date)
            .GroupBy(b => b.TrainServiceId)
            .Select(g => new { ServiceId = g.Key, Seats = g.Sum(x => x.PassengerCount) })
            .ToDictionaryAsync(x => x.ServiceId, x => x.Seats, ct);

        var results = new List<JourneyDto>();

        foreach (var s in services)
        {
            var departureAt = date.ToDateTime(s.DepartureTime);
            if (departureAt < now) continue;   // don't sell seats on a train that has gone

            var fare = fares.Calculate(s.BaseFare, departureAt, now, 1, hasRailcard);
            var seatsTaken = booked.GetValueOrDefault(s.Id, 0);

            results.Add(new JourneyDto(
                ServiceCode: s.ServiceCode,
                Operator: s.Operator,
                OriginCrs: s.Origin.Crs,
                DestinationCrs: s.Destination.Crs,
                DepartureAt: departureAt,
                ArrivalAt: date.ToDateTime(s.ArrivalTime),
                DurationMinutes: (int)(s.ArrivalTime - s.DepartureTime).TotalMinutes,
                Status: s.Status,
                DelayMinutes: s.DelayMinutes,
                SeatsRemaining: Math.Max(0, s.TotalSeats - seatsTaken),
                Fare: fare));
        }

        await TryWriteCacheAsync(cacheKey, results);
        return results;
    }

    private async Task<IReadOnlyList<JourneyDto>?> TryReadCacheAsync(string key)
    {
        if (redis is null || !redis.IsConnected) return null;

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;

            var json = (string?)value;
            return json is null ? null : JsonSerializer.Deserialize<List<JourneyDto>>(json);
        }
        catch (Exception ex)
        {
            // A cache outage must never take the endpoint down.
            logger.LogWarning(ex, "Cache read failed for {CacheKey}", key);
            return null;
        }
    }

    private async Task TryWriteCacheAsync(string key, IReadOnlyList<JourneyDto> value)
    {
        if (redis is null || !redis.IsConnected) return;

        try
        {
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), CacheTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for {CacheKey}", key);
        }
    }
}
