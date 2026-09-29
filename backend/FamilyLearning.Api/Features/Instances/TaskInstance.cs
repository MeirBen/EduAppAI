namespace FamilyLearning.Api.Features.Instances;

/// <summary>A saved AI task with frozen parameters, content and its published template revision.</summary>
/// <remarks>Parameters, content and answers remain unchanged after creation.</remarks>
public sealed class TaskInstance(Guid familyId, Guid templateVersionId, string title,
    string parametersJson, string contentJson, string generationMetadataJson)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public Guid TemplateVersionId { get; private set; } = templateVersionId;
    public string Title { get; private set; } = title;
    /// <summary>Resolved parameter values, including defaults applied at creation.</summary>
    public string ParametersJson { get; private set; } = parametersJson;
    /// <summary>The authoritative content snapshot, including answer keys; never regenerate on read.</summary>
    public string ContentJson { get; private set; } = contentJson;
    public string Status { get; private set; } = "Draft";
    /// <summary>Provider/model/prompt-version diagnostics for AI generation; never contains prompts or reasoning.</summary>
    public string? GenerationMetadataJson { get; private set; } = generationMetadataJson;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
