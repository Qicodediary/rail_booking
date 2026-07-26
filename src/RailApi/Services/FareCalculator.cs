using RailApi.Dtos;
using RailApi.Models;

namespace RailApi.Services;

public interface IFareCalculator
{
    FareBreakdown Calculate(
        decimal baseFare,
        DateTime departureAt,
        DateTime bookedAt,
        int passengerCount,
        bool hasRailcard,
        int infantCount = 0);
}

/// <summary>
/// Pure fare logic — no database, no clock, no I/O. Everything it needs is passed in,
/// which is what makes it straightforward to unit test.
/// </summary>
public class FareCalculator : IFareCalculator
{
    public const decimal OffPeakMultiplier = 0.70m;
    public const decimal AdvanceEarlyMultiplier = 0.60m;   // booked 14+ days ahead
    public const decimal AdvanceStandardMultiplier = 0.80m; // booked 7-13 days ahead
    public const decimal RailcardMultiplier = 2m / 3m;      // one third off
    public const decimal MinimumFare = 1.00m;

    private static readonly TimeOnly MorningPeakStart = new(6, 30);
    private static readonly TimeOnly MorningPeakEnd = new(9, 30);
    private static readonly TimeOnly EveningPeakStart = new(16, 0);
    private static readonly TimeOnly EveningPeakEnd = new(19, 0);

    public FareBreakdown Calculate(
        decimal baseFare,
        DateTime departureAt,
        DateTime bookedAt,
        int passengerCount,
        bool hasRailcard,
        int infantCount = 0)
    {
        if (baseFare < 0)
            throw new ArgumentOutOfRangeException(nameof(baseFare), "Base fare cannot be negative.");
        if (passengerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(passengerCount), "At least one passenger is required.");
        if (bookedAt > departureAt)
            throw new ArgumentException("Cannot book a service that has already departed.", nameof(bookedAt));

        var band = GetTimeBand(departureAt);
        var price = band == TimeBand.Peak ? baseFare : baseFare * OffPeakMultiplier;

        var advance = GetAdvanceMultiplier(departureAt, bookedAt);
        price *= advance;

        if (hasRailcard) price *= RailcardMultiplier;

        var perPassenger = Math.Max(MinimumFare, Round(price));

        return new FareBreakdown(
            BaseFare: baseFare,
            Band: band,
            AdvanceMultiplier: advance,
            RailcardApplied: hasRailcard,
            PricePerPassenger: perPassenger,
            Total: Round(perPassenger * passengerCount));
    }
///<summary>  check if it is peak or offpeak </summary>
    public static TimeBand GetTimeBand(DateTime departureAt)
    {
        if (departureAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return TimeBand.OffPeak;

        var t = TimeOnly.FromDateTime(departureAt);
        var inMorningPeak = t >= MorningPeakStart && t < MorningPeakEnd;
        var inEveningPeak = t >= EveningPeakStart && t < EveningPeakEnd;

        return inMorningPeak || inEveningPeak ? TimeBand.Peak : TimeBand.OffPeak;
    }

    private static decimal GetAdvanceMultiplier(DateTime departureAt, DateTime bookedAt)
    {
        var daysAhead = (departureAt.Date - bookedAt.Date).Days;
        return daysAhead switch
        {
            >= 14 => AdvanceEarlyMultiplier,
            >= 7 => AdvanceStandardMultiplier,
            _ => 1.00m
        };
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
