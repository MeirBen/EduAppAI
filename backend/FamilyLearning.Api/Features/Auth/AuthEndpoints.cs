using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery, IWebHostEnvironment environment) =>
        {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            context.Response.Cookies.Append("XSRF-TOKEN", token, new CookieOptions
            {
                HttpOnly = false,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
            return Results.Ok(new { token });
        });
        auth.MapPost("/login", LoginAsync).RequireRateLimiting("login");
        auth.MapPost("/logout", async (SignInManager<ParentUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization("Parent");
        auth.MapGet("/me", (HttpContext context) => Results.Ok(new
        {
            email = context.User.Identity!.Name,
            familyId = context.User.FamilyId()
        })).RequireAuthorization("Parent");
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, SignInManager<ParentUser> signIn)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Enter your email and password."] });
        var result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password,
            isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? Results.NoContent()
            : Results.Problem(statusCode: 401, title: "Sign-in failed. Check your details or try again later.");
    }

    public sealed record LoginRequest(
        [property: JsonRequired] string Email,
        [property: JsonRequired] string Password);
}
