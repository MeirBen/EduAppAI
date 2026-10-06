using FamilyLearning.Api.Features.Children;
using FamilyLearning.Api.Features.Instances;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>One retained assignment per child/snapshot pair; content remains in the immutable snapshot.</summary>
public sealed class Assignment(Guid familyId, Guid childId, Guid snapshotId, DateTime createdAtUtc)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public Guid ChildId { get; private set; } = childId;
    public Guid SnapshotId { get; private set; } = snapshotId;
    public string Status { get; private set; } = "assigned";
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; } = createdAtUtc;
    public DateTime? WithdrawnAtUtc { get; private set; }
    public Child Child { get; private set; } = null!;
    public TaskSnapshot Snapshot { get; private set; } = null!;

    /// <summary>Withdraws available work. The endpoint checks status/revision inside its write transaction.</summary>
    public void Withdraw(DateTime utcNow)
    {
        Status = "withdrawn";
        WithdrawnAtUtc = utcNow;
        Revision++;
    }
}
