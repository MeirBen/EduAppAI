using System.Net;
using System.Net.Http.Json;
using FamilyLearning.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class StaticFileCachingTests : IDisposable
{
    private readonly ApiFactory factory = new();
    private readonly WebApplicationFactory<Program> app;

    public StaticFileCachingTests()
    {
        var webRoot = Path.Combine(factory.DataDirectory, "wwwroot");
        foreach (var name in new[] { "index.html", "main-A1b_2c-3.js", "styles-A1B2C3D4.css", "media/heebo-IR7WCCQS.woff2",
            "ngsw.json", "ngsw-worker.js", "safety-worker.js", "worker-basic.min.js", "manifest.webmanifest", "favicon.svg",
            "main.js", "main-short.js", "main-A1B2C3D45.js", "page-A1B2C3D4.html", "assets-A1B2C3D4/plain.js" })
        {
            var path = Path.Combine(webRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "static test content");
        }
        app = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
    }

    [Theory]
    [InlineData("/main-A1b_2c-3.js", true)]
    [InlineData("/styles-A1B2C3D4.css", true)]
    [InlineData("/media/heebo-IR7WCCQS.woff2", true)]
    [InlineData("/", false)]
    [InlineData("/activities/new", false)]
    [InlineData("/activities/main-A1B2C3D4", false)]
    [InlineData("/index.html?file=main-A1B2C3D4.js", false)]
    [InlineData("/ngsw.json", false)]
    [InlineData("/ngsw-worker.js", false)]
    [InlineData("/safety-worker.js", false)]
    [InlineData("/worker-basic.min.js", false)]
    [InlineData("/manifest.webmanifest", false)]
    [InlineData("/favicon.svg", false)]
    [InlineData("/main.js?v=A1B2C3D4", false)]
    [InlineData("/main-short.js", false)]
    [InlineData("/main-A1B2C3D45.js", false)]
    [InlineData("/page-A1B2C3D4.html", false)]
    [InlineData("/assets-A1B2C3D4/plain.js", false)]
    public async Task Only_content_hashed_assets_are_immutable_and_validators_keep_working(string path, bool immutable)
    {
        using var client = app.CreateClient();
        var policy = immutable ? "public, max-age=31536000, immutable" : "no-cache";
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(policy, Assert.Single(response.Headers.GetValues("Cache-Control")));
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Content.Headers.LastModified);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var unchanged = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal(policy, Assert.Single(unchanged.Headers.GetValues("Cache-Control")));
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        using var head = await client.SendAsync(new(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(policy, Assert.Single(head.Headers.GetValues("Cache-Control")));
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Private_api_responses_and_missing_assets_never_inherit_public_caching()
    {
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ParentAccount>()
            .CreateAsync("cache@example.test", "Testing!Passphrase123")).Succeeded);
        await ApiFactory.RefreshCsrfAsync(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "cache@example.test", password = "Testing!Passphrase123" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var family = await client.GetAsync("/api/children");
        Assert.Equal(HttpStatusCode.OK, family.StatusCode);
        Assert.True(family.Headers.CacheControl?.NoStore);
        Assert.False(family.Headers.CacheControl?.Public);

        foreach (var path in new[] { "/missing-A1B2C3D4.js", "/api/missing-A1B2C3D4.js", "/api/missing" })
        {
            using var missing = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.NotEqual("text/html", missing.Content.Headers.ContentType?.MediaType);
            Assert.NotEqual(true, missing.Headers.CacheControl?.Public);
        }
    }

    [Theory]
    [InlineData("If-Match", "\"outdated\"", HttpStatusCode.PreconditionFailed)]
    [InlineData("Range", "bytes=1000-", HttpStatusCode.RequestedRangeNotSatisfiable)]
    [InlineData("Range", "bytes=0-4", HttpStatusCode.PartialContent)]
    public async Task Range_and_precondition_responses_are_immutable_only_on_success(string header, string value, HttpStatusCode status)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/main-A1b_2c-3.js");
        request.Headers.Add(header, value);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        var policy = status == HttpStatusCode.PartialContent ? "public, max-age=31536000, immutable" : "no-cache";
        Assert.Equal(policy, Assert.Single(response.Headers.GetValues("Cache-Control")));
    }

    public void Dispose()
    {
        app.Dispose();
        factory.Dispose();
    }
}
