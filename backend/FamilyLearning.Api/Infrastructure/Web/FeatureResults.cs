using FamilyLearning.Api.Features;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Maps expected feature failures to the API's native ProblemDetails responses.</summary>
internal static class FeatureResults
{
    internal static IResult ToHttpResult<T>(this FeatureResult<T> result) => result.ToHttpResult(static value => Results.Ok(value));

    internal static IResult ToNoContentResult(this FeatureResult<bool> result) => result.ToHttpResult(static _ => Results.NoContent());

    internal static IResult ToHttpResult<T>(this FeatureResult<T> result, Func<T, IResult> success) => result.Failure switch
    {
        null => success(result.Value!),
        { Kind: FeatureFailureKind.NotFound } => Results.NotFound(),
        { Kind: FeatureFailureKind.Unauthorized } => Results.Unauthorized(),
        { Errors: { } errors } => Results.ValidationProblem(errors),
        { Kind: FeatureFailureKind.Invalid, Title: var title } => Results.Problem(statusCode: 400, title: title),
        { Kind: FeatureFailureKind.Conflict, Title: var title } => Results.Problem(statusCode: 409, title: title),
        { Kind: FeatureFailureKind.Gone, Title: var title } => Results.Problem(statusCode: 410, title: title),
        { Kind: FeatureFailureKind.Busy, Title: null } => Results.StatusCode(429),
        { Kind: FeatureFailureKind.Busy, Title: var title } => Results.Problem(statusCode: 429, title: title),
        _ => throw new InvalidOperationException("Unsupported feature failure.")
    };
}
