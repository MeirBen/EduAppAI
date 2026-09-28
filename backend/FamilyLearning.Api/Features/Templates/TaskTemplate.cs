namespace FamilyLearning.Api.Features.Templates;

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
    public int CurrentVersion { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void PublishNextVersion(string name)
    {
        Name = name;
        CurrentVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
