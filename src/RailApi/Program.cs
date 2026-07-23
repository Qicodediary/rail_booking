using Microsoft.EntityFrameworkCore;
using RailApi.BackgroundServices;
using RailApi.Data;
using RailApi.Services;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));   // JSON lines, ready for ELK

// ---------- Services (dependency injection) ----------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<RailDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Injected rather than using DateTime.UtcNow directly, so tests can control the clock.
builder.Services.AddSingleton(TimeProvider.System);

// Redis is a cache, not a hard dependency. "abortConnect=false" means startup succeeds
// even if Redis is down; the client reconnects in the background and the search service
// checks IsConnected before using it.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddScoped<IFareCalculator, FareCalculator>();
builder.Services.AddScoped<IJourneySearchService, JourneySearchService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddHostedService<DisruptionUpdater>();

var app = builder.Build();

// ---------- Pipeline ----------
app.UseExceptionHandler();          // unhandled errors become RFC 7807 ProblemDetails
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

// ---------- Startup work ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RailDbContext>();
    await DbInitializer.InitialiseAsync(db);
}

app.Run();

// Exposed so the integration tests can spin the app up via WebApplicationFactory.
public partial class Program { }
