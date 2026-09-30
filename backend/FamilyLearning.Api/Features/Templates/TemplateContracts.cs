using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Templates;

/// <summary>List projection without the full published definition.</summary>
public sealed record TemplateSummary(Guid Id, string Name, int CurrentVersion, DateTime CreatedAtUtc);
/// <summary>A template identity paired with the published revision selected by the endpoint.</summary>
public sealed record TemplateDetail(Guid Id, int CurrentVersion, Guid VersionId, TaskTemplateDefinition Definition)
{
    /// <summary>Projects an already authorized revision, including its owning template ID.</summary>
    public static TemplateDetail From(TaskTemplateVersion version) =>
        new(version.TemplateId, version.Version, version.Id, StoredJson.Read<TaskTemplateDefinition>(version.DefinitionJson));
}
/// <summary>Publishes a replacement blueprint while detecting edits made since the caller last read it.</summary>
/// <param name="ExpectedVersion">The current revision observed by the caller; a mismatch yields HTTP 409.</param>
/// <param name="Definition">The complete replacement blueprint, validated before it is saved.</param>
public sealed record CreateVersionRequest(
    [property: JsonRequired] int ExpectedVersion,
    [property: JsonRequired] TaskTemplateDefinition Definition);

/// <summary>Immutable canonical version for the content-first lifecycle, pinned by the current template pointer.</summary>
public sealed record PlanTemplateDetail(Guid Id, int CurrentVersion, Guid VersionId, LearningPlan Definition)
{
    internal static PlanTemplateDetail From(TaskTemplateVersion version) => new(version.TemplateId, version.Version, version.Id,
        StoredJson.Read<LearningPlan>(version.DefinitionJson));
}

/// <summary>Canonical plan publication guarded against concurrent edits.</summary>
public sealed record PublishPlanRequest([property: JsonRequired] int ExpectedVersion, [property: JsonRequired] LearningPlan Definition);
