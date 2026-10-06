using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
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
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(command);
        if (command == "--create-parent") start.ArgumentList.Add("management@example.test");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Storage__Directory"] = dataDirectory;
        start.Environment["Serilog__WriteTo__File__Args__configure__0__Args__path"] = Path.Combine(dataDirectory, "logs", "server-.jsonl");
        start.Environment["Ai__ApiKey"] = "isolated-test-key";
        start.Environment["Ai__Model"] = " ";
        try
        {
            var process = await TestProcess.RunAsync(start, "TestOnly!Parent12345\nTestOnly!Parent12345\n");
            Assert.True(process.ExitCode == 0, process.Output + process.Error);
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
        Assert.False(File.Exists(Path.Combine(app.DataDirectory, "family-learning.db")));
        await MigrateAsync(app.DataDirectory);
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Https_protects_sign_in_cookies_and_parent_requests()
    {
        using var app = new ApiFactory(services =>
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443), "Production");
        await MigrateAsync(app.DataDirectory);
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
        await ApiFactory.RefreshCsrfAsync(client);
        var profile = await ChildHarness.Create(client);
        var activation = await ChildHarness.Issue(client, profile);
        using var child = app.CreateClient(new() { BaseAddress = client.BaseAddress!, AllowAutoRedirect = false });
        await ChildHarness.Csrf(child);
        using var activated = await child.PostAsJsonAsync("/api/child/auth/activate", new { code = activation["code"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        var childCookie = Assert.Single(SetCookieHeaderValue.ParseList(activated.Headers.GetValues(HeaderNames.SetCookie).ToArray()),
            cookie => cookie.Name == "FamilyLearning.Child");
        Assert.True(childCookie.Secure);
        Assert.True(childCookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, childCookie.SameSite);
        Assert.NotNull(childCookie.Expires);
        Assert.Equal("/", childCookie.Path);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await ApiFactory.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("203.0.113.9", false)]
    public async Task Only_local_proxies_can_forward_https_and_client_address(string proxyAddress, bool trusted)
    {
        using var app = new ApiFactory(services =>
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443), "Production");
        await MigrateAsync(app.DataDirectory);
        var context = await app.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("family.example.test");
            http.Request.Path = "/api/auth/csrf";
            // Appended proxy values must take precedence over client-supplied headers.
            http.Request.Headers["X-Forwarded-Proto"] = "http, https";
            http.Request.Headers["X-Forwarded-For"] = "192.0.2.123, 198.51.100.42";
            http.Request.Headers["X-Forwarded-Host"] = "untrusted.example.test";
        });

        Assert.Equal(trusted ? StatusCodes.Status200OK : StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse(trusted ? "198.51.100.42" : proxyAddress), context.Connection.RemoteIpAddress);
        Assert.Equal("family.example.test", context.Request.Host.Value);
        Assert.Equal(trusted, context.Response.Headers.ContainsKey(HeaderNames.StrictTransportSecurity));
        var cookies = context.Response.GetTypedHeaders().SetCookie;
        if (trusted)
        {
            Assert.Equal(2, cookies.Count);
            Assert.All(cookies, cookie => Assert.True(cookie.Secure));
            Assert.True(Assert.Single(cookies, cookie => cookie.Name == "FamilyLearning.Csrf").HttpOnly);
        }
        else
        {
            Assert.Empty(cookies);
            Assert.Equal("https://family.example.test/api/auth/csrf", context.Response.Headers.Location);
        }
    }

    private static async Task MigrateAsync(string directory)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--migrate");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Storage__Directory"] = directory;
        start.Environment["Serilog__WriteTo__File__Args__configure__0__Args__path"] = Path.Combine(directory, "logs", "server-.jsonl");
        // Production must apply the baseline before starting its durable worker.
        start.Environment["Ai__ApiKey"] = "";
        start.Environment["OPENROUTER_API_KEY"] = "";
        var result = await TestProcess.RunAsync(start);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }
}
