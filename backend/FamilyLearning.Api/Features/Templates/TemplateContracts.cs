using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Templates;

public sealed record TemplateSummary(Guid Id, string Name, int CurrentVersion, DateTime CreatedAtUtc);
public sealed record TemplateDetail(Guid Id, int CurrentVersion, Guid VersionId, TaskTemplateDefinition Definition)
{
    public static TemplateDetail From(TaskTemplate template, TaskTemplateVersion version) =>
        new(template.Id, version.Version, version.Id, StoredJson.Read<TaskTemplateDefinition>(version.DefinitionJson));
}
public sealed record CreateVersionRequest(int ExpectedVersion, TaskTemplateDefinition Definition);
