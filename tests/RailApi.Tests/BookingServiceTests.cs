using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RailApi.Data;
using RailApi.Dtos;
using RailApi.Models;
using RailApi.Services;
using Xunit;

namespace RailApi.Tests;

public class BookingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RailDbContext _db;
    private readonly DateTime _now = new(2026, 7, 25, 5, 0, 0);   // 固定的“现在”

    public BookingServiceTests()
    {
        // build a fake database 
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
        // set up some stations and a train service for testing
        var euston = new Station { Crs = "BRI", Name = "Bristol Temple Meads", City = "Bristol" };
        var carlisle = new Station { Crs = "PAD", Name = "London Paddington", City = "London" };
        _db.Stations.AddRange(euston, carlisle);
        _db.SaveChanges();

        _db.TrainServices.Add(new TrainService
        {
            ServiceCode = "BRIPAD0800",
            OriginStationId = euston.Id,
            DestinationStationId = carlisle.Id,
            DepartureTime = new TimeOnly(8, 0),
            ArrivalTime = new TimeOnly(11, 25),
            Operator = "Great Western Railway",
            BaseFare = 100m,
            TotalSeats = 100
        });
        _db.SaveChanges();
    }

    private BookingService CreateSut() => new(
        _db,
        new FareCalculator(),
        NullLogger<BookingService>.Instance,
        new FixedTimeProvider(_now));

    // test starts here, for example: 
    [Fact]
    public async Task Infacts_are_free_when_booking()
    {
        var sut = CreateSut();
        var request = new CreateBookingRequest(
            ServiceCode: "BRIPAD0800",
            TravelDate: new DateOnly(2026, 7, 28),
            PassengerName: "William Smith",
            PassengerCount: 2,
            HasRailcard: false,
            InfantCount: 3
        );
        var booking =await sut.CreateAsync(request);
        booking.TotalPrice.Should().Be(200.00m);
    }
    // Add another test to check if cancellation works correctly
    [Fact]
    public async Task cancell_morethan_24h_before_departure()
    {
        var sut = CreateSut();
        // creat a booking first
        var request = new CreateBookingRequest(
            ServiceCode: "BRIPAD0800",
            TravelDate: new DateOnly(2026, 7, 27),
            PassengerName: "William Smith",
            PassengerCount: 2,
            HasRailcard: false,
            InfantCount: 3
        );
        var booking = await sut.CreateAsync(request);

        // now cancelthe booking
        var cancelled = await sut.CancelAsync(booking.Reference);
        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.RefundAmount.Should().Be(booking.TotalPrice);
    }

    [Fact]
        public async Task cancell_within_24h_before_departure()
    {
        var sut = CreateSut();
        // creat a booking first
        var request = new CreateBookingRequest(
            ServiceCode: "BRIPAD0800",
            TravelDate: new DateOnly(2026, 7, 25),
            PassengerName: "William Smith",
            PassengerCount: 2,
            HasRailcard: false,
            InfantCount: 3
        );
        var booking = await sut.CreateAsync(request);

        // now cancelthe booking
        var cancelled = await sut.CancelAsync(booking.Reference);
        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.RefundAmount.Should().Be(0.00m);
    }

    [Fact]
        public async Task cancell_after_departure()
    {
        var sut = CreateSut();
        // creat a booking first
        var request = new CreateBookingRequest(
            ServiceCode: "BRIPAD0800",
            TravelDate: new DateOnly(2026, 7, 25),
            PassengerName: "William Smith",
            PassengerCount: 2,
            HasRailcard: false,
            InfantCount: 3
        );
        var booking = await sut.CreateAsync(request);


        // build a fake service passed the departure time 
        var laterSut= new BookingService(
            _db,
            new FareCalculator(),
            NullLogger<BookingService>.Instance,
            new FixedTimeProvider(new DateTime(2026, 7, 25, 10, 0,0))
        );

        // now cancel the booking
        var act = async () =>await laterSut.CancelAsync(booking.Reference); // wrap the cancellation to act 
        await act.Should().ThrowAsync<BookingException>();
    }



    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}