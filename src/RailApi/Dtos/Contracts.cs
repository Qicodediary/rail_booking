using RailApi.Models;

namespace RailApi.Dtos;

public record StationDto(string Crs, string Name, string City);

public record FareBreakdown(
    decimal BaseFare,
    TimeBand Band,
    decimal AdvanceMultiplier,
    bool RailcardApplied,
    decimal PricePerPassenger,
    decimal Total);

public record JourneyDto(
    string ServiceCode,
    string Operator,
    string OriginCrs,
    string DestinationCrs,
    DateTime DepartureAt,
    DateTime ArrivalAt,
    int DurationMinutes,
    ServiceStatus Status,
    int DelayMinutes,
    int SeatsRemaining,
    FareBreakdown Fare);

public record CreateBookingRequest(
    string ServiceCode,
    DateOnly TravelDate,
    string PassengerName,
    int PassengerCount,
    bool HasRailcard,
    int InfantCount=0,
    SeatClass SeatClass=SeatClass.Standard);

public record BookingDto(
    string Reference,
    string ServiceCode,
    string OriginCrs,
    string DestinationCrs,
    DateOnly TravelDate,
    DateTime DepartureAt,
    string PassengerName,
    int PassengerCount,
    decimal TotalPrice,
    DateTimeOffset CreatedAt,
    BookingStatus Status,
    decimal RefundAmount,
    SeatClass SeatClass);
