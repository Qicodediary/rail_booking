namespace RailApi.Models;

public class Station
{
    public int Id { get; set; }

    /// <summary>Three-letter CRS code, e.g. "EUS" for London Euston.</summary>
    public required string Crs { get; set; }

    public required string Name { get; set; }

    public required string City { get; set; }
}
