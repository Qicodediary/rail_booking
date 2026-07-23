using Microsoft.EntityFrameworkCore;
using RailApi.Models;

namespace RailApi.Data;

public static class DbInitializer
{
    public static async Task InitialiseAsync(RailDbContext db, CancellationToken ct = default)
    {
        // For a demo this is fine. In a real service you would commit EF migrations
        // and call db.Database.MigrateAsync() here instead — see README.
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Stations.AnyAsync(ct)) return;

        var stations = new List<Station>
        {
            new() { Crs = "EUS", Name = "London Euston",        City = "London" },
            new() { Crs = "KGX", Name = "London King's Cross",  City = "London" },
            new() { Crs = "PAD", Name = "London Paddington",    City = "London" },
            new() { Crs = "MAN", Name = "Manchester Piccadilly",City = "Manchester" },
            new() { Crs = "EDB", Name = "Edinburgh Waverley",   City = "Edinburgh" },
            new() { Crs = "GLC", Name = "Glasgow Central",      City = "Glasgow" },
            new() { Crs = "BRI", Name = "Bristol Temple Meads", City = "Bristol" },
            new() { Crs = "LDS", Name = "Leeds",                City = "Leeds" },
            new() { Crs = "CAR", Name = "Carlisle",             City = "Carlisle" },
            new() { Crs = "YRK", Name = "York",                 City = "York" }
        };

        db.Stations.AddRange(stations);
        await db.SaveChangesAsync(ct);

        var byCrs = stations.ToDictionary(s => s.Crs);
        var services = new List<TrainService>();

        // (origin, destination, operator, journey minutes, peak fare, seats)
        var routes = new (string From, string To, string Op, int Mins, decimal Fare, int Seats)[]
        {
            ("EUS", "MAN", "Avanti West Coast", 128, 89.50m, 220),
            ("EUS", "GLC", "Avanti West Coast", 275, 152.00m, 260),
            ("EUS", "CAR", "Avanti West Coast", 205, 121.00m, 200),
            ("KGX", "EDB", "LNER",              258, 144.00m, 300),
            ("KGX", "YRK", "LNER",              118, 76.00m,  300),
            ("KGX", "LDS", "LNER",              135, 82.50m,  280),
            ("PAD", "BRI", "GWR",               95,  64.00m,  240),
            ("MAN", "EDB", "TransPennine",      215, 98.00m,  180),
            ("CAR", "GLC", "Avanti West Coast", 72,  38.00m,  150),
            ("LDS", "MAN", "Northern",          58,  24.50m,  120)
        };

        var departures = new[] { 6, 7, 8, 9, 11, 13, 15, 16, 17, 18, 20 };

        foreach (var r in routes)
        {
            foreach (var hour in departures)
            {
                var dep = new TimeOnly(hour, hour % 2 == 0 ? 15 : 45);
                var arr = dep.AddMinutes(r.Mins);

                // Same route in both directions.
                services.Add(new TrainService
                {
                    ServiceCode = $"{r.From}{r.To}{hour:D2}{dep.Minute:D2}",
                    OriginStationId = byCrs[r.From].Id,
                    DestinationStationId = byCrs[r.To].Id,
                    DepartureTime = dep,
                    ArrivalTime = arr,
                    Operator = r.Op,
                    BaseFare = r.Fare,
                    TotalSeats = r.Seats
                });

                services.Add(new TrainService
                {
                    ServiceCode = $"{r.To}{r.From}{hour:D2}{dep.Minute:D2}",
                    OriginStationId = byCrs[r.To].Id,
                    DestinationStationId = byCrs[r.From].Id,
                    DepartureTime = dep,
                    ArrivalTime = arr,
                    Operator = r.Op,
                    BaseFare = r.Fare,
                    TotalSeats = r.Seats
                });
            }
        }

        db.TrainServices.AddRange(services);
        await db.SaveChangesAsync(ct);
    }
}
