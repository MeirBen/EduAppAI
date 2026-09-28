namespace FamilyLearning.Api.Features.Instances;

// The draft owns a snapshot. Future assignment changes status, never content.
public sealed class TaskInstance
{
    private TaskInstance() { }

    public TaskInstance(Guid familyId, Guid templateVersionId, string title,
        string parametersJson, string contentJson, int seed)
    {
        FamilyId = familyId;
        TemplateVersionId = templateVersionId;
        Title = title;
        ParametersJson = parametersJson;
        ContentJson = contentJson;
        Seed = seed;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public string Title { get; private set; } = "";
    public string ParametersJson { get; private set; } = "";
    public string ContentJson { get; private set; } = "";
    public string Status { get; private set; } = "Draft";
    public string GenerationMethod { get; private set; } = "math-v1";
    public int Seed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
