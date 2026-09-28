namespace FamilyLearning.Api.Features.Instances;

/// <summary>A draft that owns frozen parameters and content from one published template revision.</summary>
/// <remarks>Future assignment may change status, but must never replace the content snapshot.</remarks>
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
    /// <summary>Resolved parameter values, including defaults applied at creation.</summary>
    public string ParametersJson { get; private set; } = "";
    /// <summary>The authoritative content snapshot, including answer keys; never regenerate on read.</summary>
    public string ContentJson { get; private set; } = "";
    public string Status { get; private set; } = "Draft";
    public string GenerationMethod { get; private set; } = "math-v1";
    /// <summary>Diagnostic generation seed; saved content remains authoritative across code changes.</summary>
    public int Seed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
