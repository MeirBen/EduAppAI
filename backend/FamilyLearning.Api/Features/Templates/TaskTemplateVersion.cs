namespace FamilyLearning.Api.Features.Templates;

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
    public string DefinitionJson { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public string AuthoringSource { get; private set; } = "Manual";
}
