using Microsoft.EntityFrameworkCore;
using RailApi.Models;

namespace RailApi.Data;

public class RailDbContext(DbContextOptions<RailDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<TrainService> TrainServices => Set<TrainService>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Station>(e =>
        {
            e.HasIndex(s => s.Crs).IsUnique();
            e.Property(s => s.Crs).HasMaxLength(3);
            e.Property(s => s.Name).HasMaxLength(120);
            e.Property(s => s.City).HasMaxLength(80);
        });

        b.Entity<TrainService>(e =>
        {
            e.HasIndex(s => s.ServiceCode).IsUnique();
            e.Property(s => s.BaseFare).HasPrecision(8, 2);
            e.Property(s => s.Operator).HasMaxLength(80);

            // Two FKs to the same table: EF cannot infer these, so state them explicitly.
            e.HasOne(s => s.Origin)
             .WithMany()
             .HasForeignKey(s => s.OriginStationId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.Destination)
             .WithMany()
             .HasForeignKey(s => s.DestinationStationId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Reference).HasMaxLength(10);
            e.Property(x => x.PassengerName).HasMaxLength(120);
            e.Property(x => x.TotalPrice).HasPrecision(8, 2);

            // Seat-availability queries filter on this pair, so index it.
            e.HasIndex(x => new { x.TrainServiceId, x.TravelDate });
        });
    }
}
