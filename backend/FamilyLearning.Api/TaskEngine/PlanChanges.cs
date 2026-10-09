using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>An application-computed change with current/previous scope paths, never a provider summary.</summary>
public sealed record PlanChange(string Kind, string Path, string? Id = null, string? PreviousPath = null);

/// <summary>Normalizes proposal identity against the submitted base and computes actual ordered changes.</summary>
public static class PlanChanges
{
    /// <summary>Assigns new null material IDs and the application-owned schema version, preserves supplied sources, then validates.</summary>
    public static LearningPlan AssignNewIds(LearningPlan proposal, LearningPlan? previous)
    {
        var errors = new Dictionary<string, string[]>();
        var prior = previous is null ? [] : previous.Materials.ToDictionary(m => m.Id!, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        string Assign(string? id)
        {
            if (id is null)
            {
                do { id = Guid.NewGuid().ToString("N"); } while (prior.ContainsKey(id) || used.Contains(id));
            }
            else if (!prior.ContainsKey(id))
                errors.AddError("identity", "מזהה מוצע חייב להשתייך לחומר בתכנית המקורית.");
            if (!used.Add(id)) errors.AddError("identity", "מזהי התכנית חייבים להיות ייחודיים.");
            return id;
        }
        var materials = proposal.Materials?.Select(material =>
        {
            if (material is null) return null!;
            var old = material.Id is null ? null : prior.GetValueOrDefault(material.Id);
            if (old is not null && (old.Source != material.Source || old.Source == "supplied" && old.Text != material.Text))
                errors.AddError("materials", "לא ניתן לשכתב מקור קבוע דרך הצעת AI.");
            return material with { Id = Assign(material.Id) };
        }).ToArray();
        var copy = proposal with
        {
            // Like IDs, the version is the application's; providers may echo any integer.
            SchemaVersion = EngineVersions.SchemaVersion,
            Materials = materials!,
            Questions = proposal.Questions is null ? null! : proposal.Questions with
            {
                Formats = proposal.Questions.Formats?.ToArray()!
            }
        };
        foreach (var error in LearningPlanValidator.Validate(copy)) errors.AddError(error.Key, error.Value[0]);
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return copy;
    }

    /// <summary>Returns deterministic changes, including removals and moves; an identical proposal returns no changes.</summary>
    public static PlanChange[] Compare(LearningPlan? previous, LearningPlan current)
    {
        var changes = new List<PlanChange>();
        if (previous is null)
        {
            changes.Add(new("added", "plan"));
            return changes.ToArray();
        }
        void CompareField(string path, object? before, object? after)
        {
            if (!Equal(before, after)) changes.Add(new("changed", path));
        }
        CompareField("name", previous.Name, current.Name);
        CompareField("goal", previous.Goal, current.Goal);
        CompareField("guidance", previous.Guidance, current.Guidance);
        CompareField("settings", previous.Settings, current.Settings);
        CompareField("totalLength", previous.TotalLength, current.TotalLength);
        CompareField("questions", previous.Questions, current.Questions);
        var beforeEntities = Entities(previous).ToArray();
        var afterEntities = Entities(current).ToArray();
        var before = beforeEntities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var after = afterEntities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        foreach (var old in beforeEntities)
            if (!after.ContainsKey(old.Id)) changes.Add(new("removed", old.Path, old.Id));
        foreach (var entity in afterEntities)
        {
            if (!before.TryGetValue(entity.Id, out var old)) changes.Add(new("added", entity.Path, entity.Id));
            else
            {
                if (entity.Path != old.Path) changes.Add(new("moved", entity.Path, entity.Id, old.Path));
                if (!Equal(old.Value, entity.Value)) changes.Add(new("changed", entity.Path, entity.Id));
            }
        }
        return changes.ToArray();
    }

    // Records compare arrays by reference; compact JSON compares this bounded contract by value.
    private static bool Equal(object? left, object? right) => JsonSerializer.Serialize(left, EngineJson.Options) == JsonSerializer.Serialize(right, EngineJson.Options);

    private sealed record Entity(string Id, string Path, object Value);

    private static IEnumerable<Entity> Entities(LearningPlan plan)
    {
        for (var i = 0; i < plan.Materials.Length; i++)
        {
            var material = plan.Materials[i];
            yield return new(material.Id!, $"materials[{i}]", material);
        }
    }
}
