namespace RailApi.Tests;

/// <summary>A clock that never moves, so time-dependent logic is deterministic under test.</summary>
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
