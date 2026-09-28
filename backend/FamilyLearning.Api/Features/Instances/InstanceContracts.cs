using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Values for a new draft; an empty dictionary uses template defaults, while null is invalid.</summary>
public sealed record CreateInstanceRequest([property: JsonRequired] Dictionary<string, JsonElement> Parameters);
/// <summary>List projection without questions, parameters or answer keys.</summary>
public sealed record InstanceSummary(Guid Id, string Title, string Status, DateTime CreatedAtUtc);

/// <summary>A parent-only view of a saved draft, including its answer keys.</summary>
/// <remarks>Child endpoints must project a separate answer-free DTO after checking assignment access.</remarks>
public sealed record InstancePreview(Guid Id, Guid TemplateVersionId, int TemplateVersion,
    Dictionary<string, JsonElement> Parameters, TaskContent Content, string Status, DateTime CreatedAtUtc)
{
    /// <summary>Reads stored snapshots from an already authorized instance without running a generator.</summary>
    /// <param name="instance">The instance belonging to the authenticated parent's family.</param>
    /// <param name="version">Revision number of the instance's referenced template version.</param>
    public static InstancePreview From(TaskInstance instance, int version) => new(instance.Id,
        instance.TemplateVersionId, version, StoredJson.Read<Dictionary<string, JsonElement>>(instance.ParametersJson),
        StoredJson.Read<TaskContent>(instance.ContentJson), instance.Status, instance.CreatedAtUtc);
}
