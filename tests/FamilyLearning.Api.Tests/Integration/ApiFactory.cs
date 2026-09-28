using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyLearning.Api.Tests.Integration;

/// <summary>Runs the real application against a unique temporary SQLite database and key directory.</summary>
public sealed class ApiFactory(Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "family-learning-tests", Guid.NewGuid().ToString());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:Directory", dataDirectory);
        // Developer secrets/environment must never enable a real provider in automated tests.
        builder.UseSetting("Ai:ApiKey", "");
        builder.UseSetting("OPENROUTER_API_KEY", "");
        builder.UseSetting("Ai:Model", "openrouter/free");
        builder.UseSetting("Ai:Endpoint", "https://openrouter.ai/api/v1");
        builder.UseSetting("Ai:RequestTimeoutSeconds", "180");
        builder.ConfigureServices(services => services.RemoveAll<IChatClient>());
        if (configureServices is not null) builder.ConfigureServices(configureServices);
    }

    /// <summary>Provisions a new family and returns a signed-in client with its current CSRF header.</summary>
    /// <remarks>Each call creates a different family so tests can exercise ownership boundaries.</remarks>
    public async Task<HttpClient> ParentAsync()
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        using (var verification = Services.CreateScope())
        {
            var connection = verification.ServiceProvider.GetRequiredService<LearningDbContext>().Database.GetDbConnection();
            Assert.Equal(Path.Combine(dataDirectory, "family-learning.db"), connection.DataSource);
        }
        var email = $"parent-{Guid.NewGuid():N}@example.test";
        using var scope = Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ParentAccount>()
            .CreateAsync(email, "Testing!Passphrase123");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        await RefreshCsrfAsync(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Testing!Passphrase123" });
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        await RefreshCsrfAsync(client);
        return client;
    }

    public static async Task RefreshCsrfAsync(HttpClient client)
    {
        var result = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", result.GetProperty("token").GetString());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
    }
}
