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
    public TaskSession? Session { get; private set; }

    /// <summary>Advances available work after its session evaluation has been frozen in the same transaction.</summary>
    public void Submit(bool needsReview)
    {
        Status = needsReview ? "awaiting-review" : "completed";
        Revision++;
    }

    /// <summary>Completes pending work after all parent grades are saved in the same revision-checked transaction.</summary>
    public void CompleteReview()
    {
        Status = "completed";
        Revision++;
    }

    /// <summary>Withdraws available work. The endpoint checks status/revision inside its write transaction.</summary>
    public void Withdraw(DateTime utcNow)
    {
        Status = "withdrawn";
        WithdrawnAtUtc = utcNow;
        Revision++;
    }

    /// <summary>Returns withdrawn work, with its saved session. The endpoint checks status, revision and eligibility.</summary>
    public void Restore()
    {
        Status = "assigned";
        WithdrawnAtUtc = null;
        Revision++;
    }
}
