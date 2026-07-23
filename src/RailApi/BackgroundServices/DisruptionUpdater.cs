using Microsoft.EntityFrameworkCore;
using RailApi.Data;
using RailApi.Models;

namespace RailApi.BackgroundServices;

/// <summary>
/// Stands in for a real-time feed from the operators: every 30 seconds it refreshes
/// the delay/cancellation status of a random sample of services.
/// </summary>
public class DisruptionUpdater(
    IServiceScopeFactory scopeFactory,
    ILogger<DisruptionUpdater> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int SampleSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Disruption updater started (every {Seconds}s)", Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let a single bad tick kill the host.
                logger.LogError(ex, "Disruption refresh failed");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        // BackgroundService is a singleton; DbContext is scoped. Hence the explicit scope.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RailDbContext>();

        var sample = await db.TrainServices
            .OrderBy(_ => EF.Functions.Random())
            .Take(SampleSize)
            .ToListAsync(ct);

        foreach (var service in sample)
        {
            var roll = Random.Shared.Next(100);
            (service.Status, service.DelayMinutes) = roll switch
            {
                < 70 => (ServiceStatus.OnTime, 0),
                < 95 => (ServiceStatus.Delayed, Random.Shared.Next(5, 45)),
                _ => (ServiceStatus.Cancelled, 0)
            };
        }

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Refreshed disruption status for {Count} services", sample.Count);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
