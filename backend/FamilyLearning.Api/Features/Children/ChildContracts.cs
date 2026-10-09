using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Explicit replacement of optional details; omitting the object on update preserves the saved values.</summary>
public sealed record ChildProfileDetails([property: JsonRequired] string? Grade,
    [property: JsonRequired] int? Age);
public sealed record CreateChildRequest([property: JsonRequired] string Name, ChildProfileDetails? Details = null);
public sealed record UpdateChildRequest([property: JsonRequired] string Name, [property: JsonRequired] bool Enabled,
    [property: JsonRequired] long ExpectedRevision, ChildProfileDetails? Details = null);
public sealed record CreateActivationRequest([property: JsonRequired] string DeviceLabel);
/// <summary>Transient secret submitted only in the request body; never log or persist it.</summary>
public sealed record ActivateChildRequest([property: JsonRequired] string Code);
public sealed record ChildSummary(Guid Id, string Name, bool Enabled, long Revision, DateTime CreatedAtUtc,
    string? Grade, int? Age, DateTime? AgeConfirmedAtUtc, DateTime? UpdatedAtUtc, bool HasAssignments)
{
    internal static ChildSummary From(Child child, bool hasAssignments) => new(child.Id, child.Name, child.Enabled, child.Revision, child.CreatedAtUtc,
        child.Grade, child.Age, child.AgeConfirmedAtUtc, child.UpdatedAtUtc, hasAssignments);
}
/// <summary>Child bootstrap without parent identity or parent-only limits.</summary>
public sealed record ChildSessionIdentity(Guid ChildId, string Name, DateTime ExpiresAtUtc, int AnswerLength);
public sealed record ChildDeviceSummary(Guid Id, string DeviceLabel, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc, bool CanRemove);
/// <summary>Returned only by issuance; subsequent reads never disclose the code.</summary>
public sealed record ChildActivationCode(string Code, DateTime ExpiresAtUtc);

internal static class ChildValidation
{
    internal static IResult? ValidateDetails(ChildProfileDetails? details)
    {
        if (details is null) return null;
        var errors = new Dictionary<string, string[]>();
        if (details.Grade?.Trim().Length > EngineValidation.NameLength)
            errors["details.grade"] = [$"הכיתה מוגבלת ל־{EngineValidation.NameLength} תווים."];
        if (details.Age is < 0 or > EngineValidation.MaxChildAge)
            errors["details.age"] = [$"יש להזין גיל שלם בין 0 ל־{EngineValidation.MaxChildAge}."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    internal static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= EngineValidation.NameLength;
    internal static IResult InvalidName(string field) => Results.ValidationProblem(new Dictionary<string, string[]>
    { [field] = [$"יש להזין שם באורך 1–{EngineValidation.NameLength} תווים."] });
}
