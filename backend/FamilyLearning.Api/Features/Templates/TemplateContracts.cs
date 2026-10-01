using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Templates;

/// <summary>List projection without the full published definition.</summary>
public sealed record TemplateSummary(Guid Id, string Name, int CurrentVersion, DateTime CreatedAtUtc);
/// <summary>Immutable canonical version for the content-first lifecycle, pinned by the current template pointer.</summary>
public sealed record PlanTemplateDetail(Guid Id, int CurrentVersion, Guid VersionId, LearningPlan Definition)
{
    internal static PlanTemplateDetail From(TaskTemplateVersion version) => new(version.TemplateId, version.Version, version.Id,
        StoredJson.Read<LearningPlan>(version.DefinitionJson));
}

/// <summary>Canonical plan publication guarded against concurrent edits.</summary>
public sealed record PublishPlanRequest([property: JsonRequired] int ExpectedVersion, [property: JsonRequired] LearningPlan Definition);
