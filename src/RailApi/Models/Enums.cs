namespace RailApi.Models;

public enum ServiceStatus
{
    OnTime = 0,
    Delayed = 1,
    Cancelled = 2
}

public enum TimeBand
{
    Peak = 0,
    OffPeak = 1
}

public enum BookingStatus
{
    Active = 0,
    Cancelled = 1
}

public enum SeatClass
{
    Standard = 0,
    First = 1
}
