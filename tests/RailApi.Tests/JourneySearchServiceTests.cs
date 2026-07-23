using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RailApi.Data;
using RailApi.Models;
using RailApi.Services;
using Xunit;

namespace RailApi.Tests;

/// <summary>
/// Exercises the search service against a real (in-memory SQLite) database, so the
/// LINQ actually has to translate to SQL. Redis is passed as null: the cache is
/// optional by design, and this test asserts the service works without it.
/// </summary>
public class JourneySearchServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RailDbContext _db;
    private readonly DateTime _now = new(2026, 8, 5, 5, 0, 0);   // Wednesday, 05:00

    public JourneySearchServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RailDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RailDbContext(options);
        _db.Database.EnsureCreated();
        Seed();
    }

    private void Seed()
    {
        var euston = new Station { Crs = "EUS", Name = "London Euston", City = "London" };
        var carlisle = new Station { Crs = "CAR", Name = "Carlisle", City = "Carlisle" };
        _db.Stations.AddRange(euston, carlisle);
        _db.SaveChanges();

        _db.TrainServices.AddRange(
            new TrainService
            {
                ServiceCode = "EUSCAR0800",
                OriginStationId = euston.Id,
                DestinationStationId = carlisle.Id,
                DepartureTime = new TimeOnly(8, 0),
                ArrivalTime = new TimeOnly(11, 25),
                Operator = "Avanti West Coast",
                BaseFare = 100m,
                TotalSeats = 100
            },
            new TrainService
            {
                ServiceCode = "EUSCAR1200",
                OriginStationId = euston.Id,
                DestinationStationId = carlisle.Id,
                DepartureTime = new TimeOnly(12, 0),
                ArrivalTime = new TimeOnly(15, 25),
                Operator = "Avanti West Coast",
                BaseFare = 100m,
                TotalSeats = 100
            },
            new TrainService   // opposite direction: must not appear in EUS -> CAR results
            {
                ServiceCode = "CAREUS0900",
                OriginStationId = carlisle.Id,
                DestinationStationId = euston.Id,
                DepartureTime = new TimeOnly(9, 0),
                ArrivalTime = new TimeOnly(12, 25),
                Operator = "Avanti West Coast",
                BaseFare = 100m,
                TotalSeats = 100
            });

        _db.SaveChanges();
    }

    private JourneySearchService CreateSut() => new(
        _db,
        new FareCalculator(),
        redis: null,
        NullLogger<JourneySearchService>.Instance,
        new FixedTimeProvider(_now));

    [Fact]
    public async Task Returns_only_services_on_the_requested_route()
    {
        var results = await CreateSut().SearchAsync("EUS", "CAR", new DateOnly(2026, 8, 5), false);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(j => j.OriginCrs == "EUS" && j.DestinationCrs == "CAR");
    }

    [Fact]
    public async Task Station_codes_are_case_insensitive()
    {
        var results = await CreateSut().SearchAsync("eus", "car", new DateOnly(2026, 8, 5), false);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Services_that_have_already_departed_are_excluded()
    {
        // Clock is 05:00; move it past the 08:00 departure.
        var sut = new JourneySearchService(
            _db, new FareCalculator(), null,
            NullLogger<JourneySearchService>.Instance,
            new FixedTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0)));

        var results = await sut.SearchAsync("EUS", "CAR", new DateOnly(2026, 8, 5), false);

        results.Should().ContainSingle().Which.ServiceCode.Should().Be("EUSCAR1200");
    }

    [Fact]
    public async Task Fares_reflect_the_time_band_of_each_departure()
    {
        var results = await CreateSut().SearchAsync("EUS", "CAR", new DateOnly(2026, 8, 5), false);

        var peak = results.Single(j => j.ServiceCode == "EUSCAR0800");
        var offPeak = results.Single(j => j.ServiceCode == "EUSCAR1200");

        peak.Fare.Total.Should().Be(100.00m);
        offPeak.Fare.Total.Should().Be(70.00m);
    }

    [Fact]
    public async Task Seats_remaining_accounts_for_existing_bookings()
    {
        var service = _db.TrainServices.Single(s => s.ServiceCode == "EUSCAR1200");
        _db.Bookings.Add(new Booking
        {
            Reference = "TLTEST01",
            TrainServiceId = service.Id,
            TravelDate = new DateOnly(2026, 8, 5),
            PassengerName = "Test Passenger",
            PassengerCount = 4,
            TotalPrice = 280m,
            CreatedAt = _now
        });
        await _db.SaveChangesAsync();

        var results = await CreateSut().SearchAsync("EUS", "CAR", new DateOnly(2026, 8, 5), false);

        results.Single(j => j.ServiceCode == "EUSCAR1200").SeatsRemaining.Should().Be(96);
        results.Single(j => j.ServiceCode == "EUSCAR0800").SeatsRemaining.Should().Be(100);
    }

    [Fact]
    public async Task Bookings_on_another_date_do_not_reduce_availability()
    {
        var service = _db.TrainServices.Single(s => s.ServiceCode == "EUSCAR1200");
        _db.Bookings.Add(new Booking
        {
            Reference = "TLTEST02",
            TrainServiceId = service.Id,
            TravelDate = new DateOnly(2026, 8, 6),
            PassengerName = "Test Passenger",
            PassengerCount = 10,
            TotalPrice = 700m,
            CreatedAt = _now
        });
        await _db.SaveChangesAsync();

        var results = await CreateSut().SearchAsync("EUS", "CAR", new DateOnly(2026, 8, 5), false);

        results.Single(j => j.ServiceCode == "EUSCAR1200").SeatsRemaining.Should().Be(100);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
