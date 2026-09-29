using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ProductionHostTests
{
    [Theory]
    [InlineData("--migrate")]
    [InlineData("--create-parent")]
    public async Task Management_commands_do_not_depend_on_AI_configuration(string command)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "family-learning-management", Guid.NewGuid().ToString());
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(command);
        if (command == "--create-parent") start.ArgumentList.Add("management@example.test");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Storage__Directory"] = dataDirectory;
        start.Environment["Ai__ApiKey"] = "isolated-test-key";
        start.Environment["Ai__Model"] = " ";
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync("TestOnly!Parent12345\nTestOnly!Parent12345\n");
            process.StandardInput.Close();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.True(process.ExitCode == 0, await output + await error);
            Assert.True(File.Exists(Path.Combine(dataDirectory, "family-learning.db")));
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Schema_changes_require_explicit_management_command()
    {
        using var app = new ApiFactory(environment: "Production");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());

        Assert.Equal(0, await ManagementCommand.RunAsync(app.Services, ["--migrate"]));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Https_protects_sign_in_cookies_and_parent_requests()
    {
        using var app = new ApiFactory(services =>
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443), "Production");
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://family.example.test"), AllowAutoRedirect = false });
        using var redirect = await client.GetAsync("http://family.example.test/api/auth/csrf");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode);
        Assert.Equal("https://family.example.test/api/auth/csrf", redirect.Headers.Location?.AbsoluteUri);
        Assert.False(redirect.Headers.Contains(HeaderNames.SetCookie));

        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.True(csrf.Headers.Contains(HeaderNames.StrictTransportSecurity));
        Assert.True(csrf.Headers.CacheControl?.NoCache);
        Assert.True(csrf.Headers.CacheControl?.NoStore);
        Assert.Contains(csrf.Headers.Pragma, value => value.Name == "no-cache");
        var cookies = SetCookieHeaderValue.ParseList(csrf.Headers.GetValues(HeaderNames.SetCookie).ToArray());
        Assert.All(cookies, cookie => Assert.True(cookie.Secure));
        Assert.True(Assert.Single(cookies, cookie => cookie.Name == "FamilyLearning.Csrf").HttpOnly);
        Assert.False(Assert.Single(cookies, cookie => cookie.Name == "XSRF-TOKEN").HttpOnly);
        var token = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token.GetProperty("token").GetString());

        Assert.Equal(0, await ManagementCommand.RunAsync(app.Services, ["--migrate"]));
        using var scope = app.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ParentAccount>()
            .CreateAsync("production@example.test", "TestOnly!Parent12345")).Succeeded);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "production@example.test", password = "TestOnly!Parent12345" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var auth = Assert.Single(SetCookieHeaderValue.ParseList(login.Headers.GetValues(HeaderNames.SetCookie).ToArray()),
            cookie => cookie.Name == "FamilyLearning.Auth");
        Assert.True(auth.Secure);
        Assert.True(auth.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, auth.SameSite);

        using var session = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.True(session.Headers.CacheControl?.NoCache);
        Assert.True(session.Headers.CacheControl?.NoStore);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await ApiFactory.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
