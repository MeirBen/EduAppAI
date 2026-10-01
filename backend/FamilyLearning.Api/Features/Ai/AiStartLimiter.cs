using System.Threading.RateLimiting;

namespace FamilyLearning.Api.Features.Ai;

/// <summary>Shared ten-start family budget. Durable operation replay deliberately does not acquire a permit.</summary>
public sealed class AiStartLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> limiter = PartitionedRateLimiter.Create<Guid, Guid>(family =>
        RateLimitPartition.GetFixedWindowLimiter(family, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    public bool TryAcquire(Guid familyId)
    {
        using var lease = limiter.AttemptAcquire(familyId);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
