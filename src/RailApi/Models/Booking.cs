namespace RailApi.Models;

public class Booking
{
    public int Id { get; set; }

    /// <summary>Customer-facing reference, e.g. "TL7QK2XM".</summary>
    public required string Reference { get; set; }

    public int TrainServiceId { get; set; }
    public TrainService TrainService { get; set; } = null!;

    public DateOnly TravelDate { get; set; }

    public required string PassengerName { get; set; }
    public int PassengerCount { get; set; }
    public bool HasRailcard { get; set; } // default false

    public decimal TotalPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public BookingStatus Status {get; set;} = BookingStatus.Active;
    public decimal RefoundAmount {get; set;} // default 0 
}
