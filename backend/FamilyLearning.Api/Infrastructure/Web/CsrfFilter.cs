using Microsoft.AspNetCore.Antiforgery;

namespace FamilyLearning.Api.Infrastructure.Web;

/// <summary>Validates antiforgery tokens for API writes, including anonymous sign-in attempts.</summary>
public sealed class CsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    /// <summary>Passes safe methods through and returns HTTP 400 when a write lacks a valid token.</summary>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return await next(context);
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(statusCode: 400, title: "תוקף הטופס פג. יש לרענן את העמוד ולנסות שוב.");
        }
        return await next(context);
    }
}
