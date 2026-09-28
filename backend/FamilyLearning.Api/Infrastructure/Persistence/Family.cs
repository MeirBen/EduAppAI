namespace FamilyLearning.Api.Infrastructure.Persistence;

public sealed class Family
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
