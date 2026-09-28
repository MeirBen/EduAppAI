namespace FamilyLearning.Api.Features.Templates;

/// <summary>The family's stable template identity, pointing at its latest published revision.</summary>
public sealed class TaskTemplate
{
    private TaskTemplate() { }

    public TaskTemplate(Guid familyId, string name)
    {
        FamilyId = familyId;
        Name = name;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = "";
    /// <summary>One-based revision number used by EF as an optimistic concurrency token.</summary>
    public int CurrentVersion { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Advances this tracked entity to the next revision and updates its display name.</summary>
    /// <remarks>
    /// The caller must validate the definition, check the expected revision, and save this change
    /// together with a new <see cref="TaskTemplateVersion"/> in the same transaction.
    /// </remarks>
    public void PublishNextVersion(string name)
    {
        Name = name;
        CurrentVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
