using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Features.Auth;

/// <summary>Cookie-based parent sign-in, sign-out, session discovery and antiforgery token issuance.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps authentication routes onto the API group configured with CSRF protection.</summary>
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery, IWebHostEnvironment environment) =>
        {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            // Angular reads this request token; the separate authentication cookie stays HttpOnly.
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
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > ParentAccount.MaximumEmailLength ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > ParentAccount.MaximumPasswordLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["יש להזין כתובת דוא״ל וסיסמה."] });
        var result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password,
            isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? Results.NoContent()
            : Results.Problem(statusCode: 401, title: "הכניסה לא הצליחה. יש לבדוק את הפרטים או לנסות שוב מאוחר יותר.");
    }

    /// <summary>Transient sign-in credentials; never persist or log this request.</summary>
    public sealed record LoginRequest(
        [property: JsonRequired] string Email,
        [property: JsonRequired] string Password);
}
