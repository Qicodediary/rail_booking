using FluentAssertions;
using RailApi.Models;
using RailApi.Services;
using Xunit;

namespace RailApi.Tests;

public class FareCalculatorTests
{
    private readonly FareCalculator _calculator = new();

    // Wednesday 2026-08-05 is used throughout as a plain weekday.
    private static DateTime Weekday(int hour, int minute = 0) => new(2026, 8, 5, hour, minute, 0);
    private static DateTime Saturday(int hour, int minute = 0) => new(2026, 8, 8, hour, minute, 0);

    [Theory]
    [InlineData(7, 30, TimeBand.Peak)]      // morning peak
    [InlineData(6, 30, TimeBand.Peak)]      // boundary: peak starts here
    [InlineData(6, 29, TimeBand.OffPeak)]   // one minute earlier is off-peak
    [InlineData(9, 30, TimeBand.OffPeak)]   // boundary: peak has ended
    [InlineData(12, 0, TimeBand.OffPeak)]   // midday
    [InlineData(17, 0, TimeBand.Peak)]      // evening peak
    [InlineData(19, 0, TimeBand.OffPeak)]   // boundary: evening peak has ended
    public void GetTimeBand_classifies_weekday_departures(int hour, int minute, TimeBand expected)
    {
        FareCalculator.GetTimeBand(Weekday(hour, minute)).Should().Be(expected);
    }

    [Fact]
    public void Weekend_peak_hours_are_still_off_peak()
    {
        FareCalculator.GetTimeBand(Saturday(8)).Should().Be(TimeBand.OffPeak);
    }

    [Fact]
    public void Peak_walk_up_fare_is_the_full_base_fare()
    {
        var departure = Weekday(8);
        var result = _calculator.Calculate(100m, departure, departure.AddDays(-1), 1, false);

        result.Band.Should().Be(TimeBand.Peak);
        result.AdvanceMultiplier.Should().Be(1.00m);
        result.Total.Should().Be(100.00m);
    }

    [Fact]
    public void Off_peak_departure_gets_the_off_peak_discount()
    {
        var departure = Weekday(12);
        var result = _calculator.Calculate(100m, departure, departure.AddDays(-1), 1, false);

        result.Total.Should().Be(70.00m);
    }

    [Theory]
    [InlineData(20, 0.60)]   // 14+ days ahead
    [InlineData(14, 0.60)]   // boundary
    [InlineData(13, 0.80)]   // 7-13 days ahead
    [InlineData(7, 0.80)]    // boundary
    [InlineData(6, 1.00)]    // inside a week: no advance discount
    [InlineData(0, 1.00)]    // same day
    public void Advance_discount_depends_on_days_booked_ahead(int daysAhead, decimal expectedMultiplier)
    {
        var departure = Weekday(8);
        var bookedAt = departure.AddDays(-daysAhead);

        var result = _calculator.Calculate(100m, departure, bookedAt, 1, false);

        result.AdvanceMultiplier.Should().Be(expectedMultiplier);
    }

    [Fact]
    public void Discounts_compound_off_peak_then_advance_then_railcard()
    {
        var departure = Weekday(12);                 // off-peak  -> x0.70
        var bookedAt = departure.AddDays(-30);       // 14+ days  -> x0.60
        var result = _calculator.Calculate(100m, departure, bookedAt, 1, hasRailcard: true);

        // 100 * 0.70 * 0.60 * (2/3) = 28.00
        result.Total.Should().Be(28.00m);
        result.RailcardApplied.Should().BeTrue();
    }

    [Fact]
    public void Total_scales_with_passenger_count()
    {
        var departure = Weekday(8);
        var result = _calculator.Calculate(50m, departure, departure.AddDays(-1), 3, false);

        result.PricePerPassenger.Should().Be(50.00m);
        result.Total.Should().Be(150.00m);
    }
    [Fact]
    public void Infant_passengers_are_free()
    {
        var departure = Weekday(8);// weekday 8 am , original fare 
        var result = _calculator.Calculate(100m, departure, departure.AddDays(-1), 2, false, infantCount: 3);
                                      // baseFare, departure 8am, day 1 before departure, passagercount , if having railcard, infantCount is 3
        result.Total.Should().Be(200.00m);
    }

    [Fact]
    public void First_class_passengers_pay_a_supplement()
    {
        var departure = Weekday(8);// weekday 8 am , original fare 
        var result = _calculator.Calculate(100m, departure, departure.AddDays(-1), 1, false, infantCount: 0, seatClass: SeatClass.First);
                                     
        result.Total.Should().Be(150.00m);
    }

    [Fact]
    public void First_class_passengers_pay_a_supplement_offpeak()
    {
        var departure = Weekday(11);// weekday 8 am , original fare 
        var result = _calculator.Calculate(100m, departure, departure.AddDays(-1), 1, false, infantCount: 0, seatClass: SeatClass.First);
                                     
        result.Total.Should().Be(105.00m);
    }

    [Fact]
    public void Fare_never_falls_below_the_minimum()
    {
        var departure = Weekday(12);
        var result = _calculator.Calculate(0.50m, departure, departure.AddDays(-30), 1, true);

        result.PricePerPassenger.Should().Be(FareCalculator.MinimumFare);
    }

    [Fact]
    public void Rounding_is_to_two_decimal_places()
    {
        var departure = Weekday(12);
        // 33.33 * 0.70 = 23.331 -> 23.33
        var result = _calculator.Calculate(33.33m, departure, departure.AddDays(-1), 1, false);

        result.PricePerPassenger.Should().Be(23.33m);
    }

    [Fact]
    public void Booking_a_departed_service_is_rejected()
    {
        var departure = Weekday(8);
        var act = () => _calculator.Calculate(50m, departure, departure.AddHours(1), 1, false);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Passenger_count_must_be_positive(int passengers)
    {
        var departure = Weekday(8);
        var act = () => _calculator.Calculate(50m, departure, departure.AddDays(-1), passengers, false);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
