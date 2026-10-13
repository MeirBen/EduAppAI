using System.Security.Claims;
using FamilyLearning.Api.Features.Auth;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Child-only activation, identity discovery and disconnect, including HTTP cookie issuance and removal.</summary>
public static class ChildAuthEndpoints
{
    public static void MapChildAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapGet("/csrf", AuthEndpoints.Csrf).AllowAnonymous().WithMetadata(new DeviceSessionEntry(ChildAuthentication.Scheme));
        auth.MapPost("/activate", ActivateAsync).AllowAnonymous().RequireRateLimiting("child-redemption")
            .WithMetadata(new DeviceSessionEntry(ChildAuthentication.Scheme, RequireAnonymous: true));
        auth.MapGet("/me", async (ClaimsPrincipal user, ChildAuthService service, CancellationToken ct) =>
            (await service.MeAsync(user, ct)).ToHttpResult());
        auth.MapPost("/logout", LogoutAsync);
    }

    private static async Task<IResult> ActivateAsync(ActivateChildRequest request, HttpContext context, ChildAuthService service, CancellationToken ct)
    {
        var result = await service.ActivateAsync(request, ct);
        if (result.Failure is not null) return result.ToHttpResult();
        var session = result.Value!;
        await context.SignInAsync(ChildAuthentication.Scheme, ChildAuthentication.Principal(session.Identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = new DateTimeOffset(session.IssuedAtUtc),
                ExpiresUtc = new DateTimeOffset(session.ExpiresAtUtc),
                AllowRefresh = false
            });
        // The next /csrf read must obtain a token bound to the newly issued identity.
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, ChildAuthService service, CancellationToken ct)
    {
        var result = await service.LogoutAsync(context.User, ct);
        if (result.Failure is not null) return result.ToNoContentResult();
        await context.SignOutAsync(ChildAuthentication.Scheme);
        return Results.NoContent();
    }
}
