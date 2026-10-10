using System.Security.Cryptography;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>Derives detached stage requirements from the concrete activity plan.</summary>
public static class TaskRequestResolver
{
    /// <summary>Hashes the requirements content depends on; neither an engine revision nor title/instruction guidance stales it.</summary>
    public static string Fingerprint(ResolvedTaskRequest request) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.SchemaVersion,
            request.Goal,
            request.Guidance,
            request.Settings,
            request.Materials,
            Questions = request.Questions with { Formats = request.Questions.Formats.Order(StringComparer.Ordinal).ToArray() },
            request.TotalLength
        }, EngineJson.Options))).ToLowerInvariant();

    /// <summary>Derives requirements or reports bounded validation errors.</summary>
    public static ResolvedTaskRequest ResolveOrThrow(LearningPlan plan)
    {
        var resolution = Resolve(plan);
        return resolution.Value ?? throw new TaskValidationException(resolution.Errors);
    }

    /// <summary>Validates before allocating stage inputs; source strings stay exact.</summary>
    public static TaskResolution Resolve(LearningPlan plan)
    {
        var errors = LearningPlanValidator.Validate(plan);
        if (errors.Count != 0) return new(null, errors);
        return new(new(plan.SchemaVersion, EngineVersions.Revision, plan.Goal, plan.Guidance,
            plan.Settings, plan.Materials.Select(m => new ResolvedMaterial(m.Id!, m.Label, m.Source,
                m.Guidance, m.Text, LearningPlanValidator.ResolveLength(m.Length))).ToArray(),
            new(plan.Questions.Formats.ToArray(), plan.Questions.ChoiceCount, plan.Questions.Guidance),
            LearningPlanValidator.ResolveLength(plan.TotalLength), plan.DocumentGuidance), errors);
    }
}
