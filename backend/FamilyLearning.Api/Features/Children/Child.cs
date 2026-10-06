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

    public void Update(string name, bool enabled)
    {
        Name = name;
        Enabled = enabled;
        Revision++;
    }
}
