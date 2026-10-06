using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Features.Children;

public sealed record CreateChildRequest([property: JsonRequired] string Name);
public sealed record UpdateChildRequest([property: JsonRequired] string Name, [property: JsonRequired] bool Enabled,
    [property: JsonRequired] long ExpectedRevision);
public sealed record CreateActivationRequest([property: JsonRequired] string DeviceLabel);
/// <summary>Transient secret submitted only in the request body; never log or persist it.</summary>
public sealed record ActivateChildRequest([property: JsonRequired] string Code);
public sealed record ChildSummary(Guid Id, string Name, bool Enabled, long Revision, DateTime CreatedAtUtc)
{
    internal static ChildSummary From(Child child) => new(child.Id, child.Name, child.Enabled, child.Revision, child.CreatedAtUtc);
}
/// <summary>Child bootstrap without parent identity or parent-only limits.</summary>
public sealed record ChildSessionIdentity(Guid ChildId, string Name, DateTime ExpiresAtUtc, int AnswerLength);
public sealed record ChildDeviceSummary(Guid Id, string DeviceLabel, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc);
/// <summary>Returned only by issuance; subsequent reads never disclose the code.</summary>
public sealed record ChildActivationCode(string Code, DateTime ExpiresAtUtc);

internal static class ChildValidation
{
    internal static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= EngineValidation.NameLength;
    internal static IResult InvalidName(string field) => Results.ValidationProblem(new Dictionary<string, string[]>
    { [field] = [$"יש להזין שם באורך 1–{EngineValidation.NameLength} תווים."] });
}
