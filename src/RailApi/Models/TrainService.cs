namespace RailApi.Models;

/// <summary>A scheduled service that runs daily between two stations.</summary>
public class TrainService
{
    public int Id { get; set; }

    public required string ServiceCode { get; set; }

    public int OriginStationId { get; set; }
    public Station Origin { get; set; } = null!;

    public int DestinationStationId { get; set; }
    public Station Destination { get; set; } = null!;

    public TimeOnly DepartureTime { get; set; }
    public TimeOnly ArrivalTime { get; set; }

    public required string Operator { get; set; }

    /// <summary>Undiscounted peak-time fare for one adult.</summary>
    public decimal BaseFare { get; set; }

    public int TotalSeats { get; set; }

    public ServiceStatus Status { get; set; } = ServiceStatus.OnTime;
    public int DelayMinutes { get; set; }
}
