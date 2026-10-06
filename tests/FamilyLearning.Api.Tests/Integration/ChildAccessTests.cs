using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Children;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ChildAccessTests
{
    [Fact]
    public async Task Reset_between_identity_access_check_and_projection_returns_unauthorized()
    {
        var pause = new PausedIdentityRead();
        await using var h = new ChildHarness(services => services.AddScoped(provider => new LearningDbContext(
            new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>())
                .AddInterceptors(pause).Options)));
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        pause.Enabled = true;
        var reading = child.GetAsync("/api/child/auth/me");
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/templates")).StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await reading).StatusCode);
    }

    [Fact]
    public async Task A_revoked_grant_is_rechecked_inside_a_previously_authenticated_write()
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var devices = (await parent.GetFromJsonAsync<JsonNode>(Path(profile) + "/devices"))!["items"]!.AsArray();
        var grantId = Assert.Single(devices)!["id"]!.GetValue<Guid>();
        pause.Arm();
        var logout = child.PostAsync("/api/child/auth/logout", null);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(profile) + "/devices/" + grantId)).StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await logout).StatusCode);
    }

    [Fact]
    public async Task Names_and_required_members_are_validated_without_issuing_a_credential()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        foreach (var deviceLabel in new[] { "", "  ", new string('x', 101), null })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(profile) + "/activation", new { deviceLabel })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync(Path(profile), new { name = "", enabled = true, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync(Path(profile), new { name = "a", enabled = true })).StatusCode);
        var code = await Issue(parent, profile, "  device  ");
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var slot = await db.ChildActivations.SingleAsync();
        Assert.Equal("device", slot.DeviceLabel);
        Assert.Equal(64, slot.CodeHash.Length);
        Assert.NotEqual(code["code"]!.GetValue<string>(), slot.CodeHash);
        Assert.Equal(DateTimeKind.Utc, slot.ExpiresAtUtc.Kind);
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("redeem")]
    [InlineData("logout")]
    public async Task A_disable_committed_after_authentication_prevents_the_later_write(string operation)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = operation == "logout" ? await h.Activate(parent, profile) : h.App.CreateClient();
        var code = await Issue(parent, profile);
        await Csrf(child);
        pause.Arm();
        var writing = operation switch
        {
            "issue" => parent.PostAsJsonAsync(Path(profile) + "/activation", new { deviceLabel = "new" }),
            "redeem" => child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() }),
            _ => child.PostAsync("/api/child/auth/logout", null)
        };
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "disabled", enabled = false, expectedRevision = 1 })).StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(operation == "issue" ? HttpStatusCode.Conflict : operation == "redeem" ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized,
            (await writing).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.False(await db.ChildActivations.AnyAsync());
        Assert.False(await db.ChildDeviceGrants.AnyAsync(g => g.RevokedAtUtc == null));
    }

    [Fact]
    public async Task Redemption_process_limit_applies_across_distinct_source_ips()
    {
        await using var h = new ChildHarness();
        using var client = h.App.CreateClient(new() { HandleCookies = false });
        using var csrf = await client.GetAsync("/api/child/auth/csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<JsonNode>())!["token"]!.GetValue<string>();
        var cookies = string.Join("; ", csrf.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        for (var i = 0; i <= 120; i++)
        {
            var response = await h.App.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse($"192.0.2.{i / 10 + 1}");
                context.Request.Method = "POST";
                context.Request.Path = "/api/child/auth/activate";
                context.Request.Headers.Cookie = cookies;
                context.Request.Headers["X-XSRF-TOKEN"] = token;
                context.Request.ContentType = "application/json";
                var body = System.Text.Encoding.UTF8.GetBytes("{\"code\":\"invalid\"}");
                context.Request.ContentLength = body.Length;
                context.Request.Body = new MemoryStream(body);
            });
            Assert.Equal(i == 120 ? 429 : 400, response.Response.StatusCode);
        }
    }

    [Fact]
    public async Task Reset_removes_all_child_access_past_list_limits_and_preserves_another_family()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        var foreign = await Create(stranger);
        using var child = await h.Activate(parent, profile);
        using var foreignClient = await h.Activate(stranger, foreign);
        var code = await Issue(parent, profile);
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var family = (await db.Children.SingleAsync(c => c.Id == profile["id"]!.GetValue<Guid>())).FamilyId;
            for (var i = 0; i < 101; i++)
            {
                var c = new Child(family, "extra", h.Clock.Now.UtcDateTime);
                db.AddRange(c, new ChildActivation(c.Id, i.ToString(), "device", h.Clock.Now.UtcDateTime.AddMinutes(10)),
                    new ChildDeviceGrant(c.Id, "device", h.Clock.Now.UtcDateTime, h.Clock.Now.UtcDateTime.AddDays(30)));
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        await Csrf(child);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await foreignClient.GetAsync("/api/child/auth/me")).StatusCode);
        using var verification = h.App.Services.CreateScope();
        var remaining = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Single(await remaining.Children.ToArrayAsync());
        Assert.Single(await remaining.ChildDeviceGrants.ToArrayAsync());
        Assert.Single(await remaining.ChildActivations.ToArrayAsync());
    }

    [Fact]
    public async Task Anonymous_and_other_identity_tokens_cannot_authorize_child_writes_and_mixed_cookies_conflict()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        var code = await Issue(parent, profile);
        using var child = h.App.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() })).StatusCode);
        await Csrf(child);
        using var activation = await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsync("/api/child/auth/logout", null)).StatusCode);
        await Csrf(child);
        child.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        child.DefaultRequestHeaders.Add("X-XSRF-TOKEN", parent.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN"));
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsync("/api/child/auth/logout", null)).StatusCode);
        var cookie = activation.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("FamilyLearning.Child=", StringComparison.Ordinal)).Split(';')[0];
        parent.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.GetAsync("/api/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.GetAsync("/api/child/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/auth/login", new { email = "test@example.test", password = "bad" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/child/auth/activate", new { code = "invalid" })).StatusCode);
    }

    [Fact]
    public async Task Separate_cookie_policies_and_identity_bound_csrf_protect_both_modes()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent, "  נועה  ");
        Assert.Equal("נועה", profile["name"]!.GetValue<string>());
        using var child = await h.Activate(parent, profile);
        Assert.Equal(HttpStatusCode.Unauthorized, (await parent.GetAsync("/api/child/auth/me")).StatusCode);
        foreach (var path in new[] { "/api/auth/me", "/api/templates", "/api/instances", "/api/limits" })
        {
            var denied = await child.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.DoesNotContain("familyId", await denied.Content.ReadAsStringAsync());
        }
        var me = (await child.GetFromJsonAsync<JsonNode>("/api/child/auth/me"))!;
        Assert.Equal(new[] { "answerLength", "childId", "expiresAtUtc", "name" }, me.AsObject().Select(p => p.Key).Order().ToArray());
        Assert.Equal(200, me["answerLength"]!.GetValue<int>());
        Assert.True((await child.GetAsync("/api/child/auth/me")).Headers.CacheControl!.NoStore);
        using var childConflict = await child.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.Conflict, childConflict.StatusCode);
        Assert.False(childConflict.Headers.Contains("Set-Cookie"));
        Assert.Equal("urn:family-learning:device-session-conflict", (await childConflict.Content.ReadFromJsonAsync<JsonNode>())!["type"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.GetAsync("/api/child/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await child.PostAsJsonAsync("/api/auth/login", new { email = "a@example.test", password = "Testing!Passphrase123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/child/auth/activate", new { code = "bad" })).StatusCode);
        child.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsync("/api/child/auth/logout", null)).StatusCode);
        await Csrf(child);
        Assert.Equal(HttpStatusCode.NoContent, (await child.PostAsync("/api/child/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Profile_ownership_validation_revisions_and_disable_revoke_access()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        foreach (var name in new[] { "", "  ", new string('x', 101), null })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/children", new { name })).StatusCode);
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var pending = await Issue(parent, profile);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(Path(profile), new { name = "a", enabled = false, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Path(profile) + "/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(Path(profile) + "/activation", new { deviceLabel = "a" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "שם", enabled = false, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(profile), new { name = "ישן", enabled = true, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(profile) + "/activation", new { deviceLabel = "a" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "שם", enabled = true, expectedRevision = 2 })).StatusCode);
        await Csrf(child);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync("/api/child/auth/activate", new { code = pending["code"]!.GetValue<string>() })).StatusCode);
        using var renewed = await h.Activate(parent, profile);
        Assert.Equal(HttpStatusCode.OK, (await renewed.GetAsync("/api/child/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Activation_replacement_expiry_and_concurrent_redemption_never_reuse_a_code()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        var old = await Issue(parent, profile);
        var latest = await Issue(parent, profile);
        using var first = h.App.CreateClient();
        using var second = h.App.CreateClient();
        await Csrf(first);
        await Csrf(second);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/child/auth/activate", new { code = old["code"]!.GetValue<string>() })).StatusCode);
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/child/auth/activate", new { code = latest["code"]!.GetValue<string>() }),
            second.PostAsJsonAsync("/api/child/auth/activate", new { code = latest["code"]!.GetValue<string>() }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        var cookie = responses.Single(r => r.IsSuccessStatusCode).Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("FamilyLearning.Child=", StringComparison.Ordinal));
        Assert.Contains("expires=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        var devices = (await parent.GetFromJsonAsync<JsonNode>(Path(profile) + "/devices"))!;
        Assert.Single(devices["items"]!.AsArray());
        var expired = await Issue(parent, profile);
        h.Clock.Now += TimeSpan.FromMinutes(10);
        using var third = h.App.CreateClient();
        await Csrf(third);
        Assert.Equal(HttpStatusCode.BadRequest, (await third.PostAsJsonAsync("/api/child/auth/activate", new { code = expired["code"]!.GetValue<string>() })).StatusCode);
        var active = responses[0].IsSuccessStatusCode ? first : second;
        h.Clock.Now += TimeSpan.FromDays(16);
        var read = await active.GetAsync("/api/child/auth/me");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.False(read.Headers.Contains("Set-Cookie"));
        h.Clock.Now += TimeSpan.FromDays(14);
        Assert.Equal(HttpStatusCode.Unauthorized, (await active.GetAsync("/api/child/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Device_revoke_is_owned_idempotent_and_immediately_invalidates_the_cookie()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var devices = (await parent.GetFromJsonAsync<JsonNode>(Path(profile) + "/devices"))!["items"]!.AsArray();
        var device = Assert.Single(devices)!;
        Assert.Equal(new[] { "createdAtUtc", "deviceLabel", "expiresAtUtc", "id", "revokedAtUtc" }, device.AsObject().Select(p => p.Key).Order().ToArray());
        var path = Path(profile) + "/devices/" + device["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Lists_validate_pagination_and_reach_children_past_one_hundred()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        for (var i = 0; i < 101; i++) await Create(parent, i.ToString());
        foreach (var query in new[] { "page=0", "pageSize=101", "pageSize=0", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.GetAsync("/api/children?" + query)).StatusCode);
        var first = (await parent.GetFromJsonAsync<JsonNode>("/api/children?pageSize=100"))!;
        var second = (await parent.GetFromJsonAsync<JsonNode>("/api/children?pageSize=100&page=2"))!;
        Assert.Equal(100, first["items"]!.AsArray().Count);
        Assert.True(first["hasMore"]!.GetValue<bool>());
        Assert.Single(second["items"]!.AsArray());
        Assert.False(second["hasMore"]!.GetValue<bool>());
        Assert.Equal(101, first["items"]!.AsArray().Concat(second["items"]!.AsArray()).Select(c => c!["id"]!.GetValue<Guid>()).Distinct().Count());
    }

    [Fact]
    public async Task Issuance_and_redemption_have_bounded_unqueued_rate_limits()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        for (var i = 0; i < 10; i++) await Issue(parent, profile);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await parent.PostAsJsonAsync(Path(profile) + "/activation", new { deviceLabel = "a" })).StatusCode);
        using var client = h.App.CreateClient();
        await Csrf(client);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/child/auth/activate", new { code = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/child/auth/activate", new { code = "invalid" })).StatusCode);
    }
    private sealed class PausedIdentityRead : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // The identity projection reads the name and expiry together; access checks read only IDs.
            if (Enabled && command.CommandText.Contains("\"Name\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"ExpiresAtUtc\"", StringComparison.Ordinal))
            {
                Enabled = false;
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

}
