using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Instances;

public sealed record CreateInstanceRequest(Dictionary<string, JsonElement> Parameters);
public sealed record InstanceSummary(Guid Id, string Title, string Status, DateTime CreatedAtUtc);

// Parent-only DTO: contains answer keys. Child endpoints must project a separate answer-free DTO.
public sealed record InstancePreview(Guid Id, Guid TemplateVersionId, int TemplateVersion,
    Dictionary<string, JsonElement> Parameters, TaskContent Content, string Status, DateTime CreatedAtUtc)
{
    public static InstancePreview From(TaskInstance instance, int version) => new(instance.Id,
        instance.TemplateVersionId, version, StoredJson.Read<Dictionary<string, JsonElement>>(instance.ParametersJson),
        StoredJson.Read<TaskContent>(instance.ContentJson), instance.Status, instance.CreatedAtUtc);
}
