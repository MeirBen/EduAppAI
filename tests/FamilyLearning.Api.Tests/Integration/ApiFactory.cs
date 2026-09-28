using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "family-learning-tests", Guid.NewGuid().ToString());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:Directory", dataDirectory);
    }

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
