using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Configures Identity, independent parent/child authorization and same-origin cookie/antiforgery behavior.</summary>
public static class AuthConfiguration
{
    /// <summary>Registers the services used by parent endpoints and local account provisioning.</summary>
    /// <remarks><paramref name="development"/> allows HTTP cookies locally; other environments require HTTPS.</remarks>
    public static void AddParentAuthentication(this IServiceCollection services, bool development)
    {
        services.TryAddSingleton(TimeProvider.System);
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
        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddCookie(ChildAuthentication.Scheme, options =>
        {
            options.Cookie.Name = "FamilyLearning.Child";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = ChildAuthentication.ValidateAsync;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
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
            policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser().RequireRole("Parent").RequireClaim("family_id"))
            .AddPolicy("Child", policy => policy.AddAuthenticationSchemes(ChildAuthentication.Scheme)
                .RequireAuthenticatedUser().RequireRole("Child").RequireClaim("family_id").RequireClaim("device_grant_id"));
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
