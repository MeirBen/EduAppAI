namespace FamilyLearning.Api.Features.Templates;

/// <summary>An immutable published definition; existing instances retain a reference to this revision.</summary>
public sealed class TaskTemplateVersion
{
    private TaskTemplateVersion() { }

    public TaskTemplateVersion(Guid templateId, int version, string definitionJson)
    {
        TemplateId = templateId;
        Version = version;
        DefinitionJson = definitionJson;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TemplateId { get; private set; }
    public int Version { get; private set; }
    /// <summary>Validated blueprint serialized with the application's stored JSON format.</summary>
    public string DefinitionJson { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
