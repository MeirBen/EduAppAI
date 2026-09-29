using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Configures Identity, parent authorization and same-origin cookie/antiforgery behavior.</summary>
public static class AuthConfiguration
{
    /// <summary>Registers the services used by parent endpoints and local account provisioning.</summary>
    /// <param name="services">The application's dependency injection registrations.</param>
    /// <param name="development">Allows HTTP cookies for local development; other environments require HTTPS.</param>
    public static void AddParentAuthentication(this IServiceCollection services, bool development)
    {
        services.AddIdentityCore<ParentUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
            .AddEntityFrameworkStores<LearningDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<ParentPrincipalFactory>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "FamilyLearning.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            // API clients need status codes, not Identity's default HTML redirects.
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAuthorizationBuilder().AddPolicy("Parent", policy =>
            policy.RequireAuthenticatedUser().RequireRole("Parent").RequireClaim("family_id"));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "FamilyLearning.Csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddScoped<ParentAccount>();
    }
}
