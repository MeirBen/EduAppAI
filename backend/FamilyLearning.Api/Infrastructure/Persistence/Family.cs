namespace FamilyLearning.Api.Infrastructure.Persistence;

/// <summary>The ownership boundary shared by parents, children and their learning records.</summary>
public sealed class Family
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
