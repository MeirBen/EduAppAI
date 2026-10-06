namespace FamilyLearning.Api.Features.Children;

/// <summary>Family-owned profile. Disabling retains learning history; endpoints revoke access atomically.</summary>
public sealed class Child(Guid familyId, string name, DateTime createdAtUtc)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public string Name { get; private set; } = name;
    public bool Enabled { get; private set; } = true;
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; } = createdAtUtc;
    public DateTime? UpdatedAtUtc { get; private set; }
    public string? Grade { get; private set; }
    public int? Age { get; private set; }
    public DateTime? AgeConfirmedAtUtc { get; private set; }

    public void SetDetails(ChildProfileDetails details, DateTime now)
    {
        Grade = string.IsNullOrWhiteSpace(details.Grade) ? null : details.Grade.Trim();
        if (details.Age is null) AgeConfirmedAtUtc = null;
        else if (Age != details.Age) AgeConfirmedAtUtc = now;
        Age = details.Age;
    }

    public void Update(string name, bool enabled, DateTime now)
    {
        Name = name;
        Enabled = enabled;
        UpdatedAtUtc = now;
        Revision++;
    }
}
