namespace FamilyLearning.Api.Features;

/// <summary>A feature's value or expected failure, independent of HTTP response handling.</summary>
internal readonly record struct FeatureResult<T>(T? Value, FeatureFailure? Failure = null)
{
    public static implicit operator FeatureResult<T>(T value) => new(value);
    public static implicit operator FeatureResult<T>(FeatureFailure failure) => new(default, failure);
}

internal enum FeatureFailureKind { NotFound, Unauthorized, Invalid, Conflict, Gone, Busy }

internal sealed record FeatureFailure(FeatureFailureKind Kind, string? Title = null, Dictionary<string, string[]>? Errors = null)
{
    internal static readonly FeatureFailure NotFound = new(FeatureFailureKind.NotFound);
    internal static readonly FeatureFailure Unauthorized = new(FeatureFailureKind.Unauthorized);
    internal static FeatureFailure Invalid(string field, string message) => Invalid(new Dictionary<string, string[]> { [field] = [message] });
    internal static FeatureFailure Invalid(Dictionary<string, string[]> errors) => new(FeatureFailureKind.Invalid, Errors: errors);
    internal static FeatureFailure Invalid(string title) => new(FeatureFailureKind.Invalid, title);
    internal static FeatureFailure Conflict(string title) => new(FeatureFailureKind.Conflict, title);
    internal static FeatureFailure Gone(string title) => new(FeatureFailureKind.Gone, title);
    internal static FeatureFailure Busy(string? title = null) => new(FeatureFailureKind.Busy, title);
}
