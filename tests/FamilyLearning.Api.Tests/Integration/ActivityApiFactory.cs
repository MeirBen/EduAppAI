using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Integration;

/// <summary>Exercises the staged content-first route composition with real auth and a fresh disposable SQLite schema.</summary>
internal sealed class ActivityApiFactory : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"activity-tests-{Guid.NewGuid():N}");
    private WebApplication? app;
    internal IServiceProvider Services => app!.Services;

    internal async Task<HttpClient> ParentAsync(Action<IServiceCollection>? configure = null)
    {
        if (app is null)
        {
            Directory.CreateDirectory(directory);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddDbContext<LearningDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(directory, "test.db")};Foreign Keys=True;Pooling=False"));
            builder.Services.AddParentAuthentication(true);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddApplicationApi();
            builder.Services.AddActivityGeneration(new ConfigurationBuilder().Build());
            builder.Services.RemoveAll<IHostedService>();
            configure?.Invoke(builder.Services);
            app = builder.Build();
            using (var scope = app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<LearningDbContext>().Database.EnsureCreatedAsync();
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapContentFirstApi();
            await app.StartAsync();
        }
        var client = app.GetTestClient();
        var cookies = new CookieContainer();
        using var session = app.Services.CreateScope();
        var email = $"parent-{Guid.NewGuid():N}@example.test";
        var created = await session.ServiceProvider.GetRequiredService<ParentAccount>().CreateAsync(email, "Testing!Passphrase123");
        Assert.True(created.Succeeded);
        // TestServer's client has no cookie container; retain the real Identity/antiforgery cookies explicitly.
        await TokenAsync(client, cookies);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Testing!Passphrase123" });
        Assert.True(login.IsSuccessStatusCode);
        Cookies(client, login, cookies);
        await TokenAsync(client, cookies);
        return client;
    }


    private static async Task TokenAsync(HttpClient client, CookieContainer cookies)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Cookies(client, response, cookies);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", body.GetProperty("token").GetString());
    }

    private static void Cookies(HttpClient client, HttpResponseMessage response, CookieContainer cookies)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (var value in values) cookies.SetCookies(client.BaseAddress!, value);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookies.GetCookieHeader(client.BaseAddress!));
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
