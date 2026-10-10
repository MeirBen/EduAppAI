using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Library;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LibraryChangeTests
{
    private const string StreamPath = "/api/library/changes";

    [Fact]
    public async Task Child_writes_notify_parent_streams_after_commit_and_failures_publish_nothing()
    {
        await using var app = new ChildHarness();
        using var parent = await app.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        using var child = await app.Activate(parent, profile);
        var content = await ChildSessionTests.MixedSnapshot(parent);
        var assignment = await AssignmentTests.Assign(parent, profile, content.Id);
        var path = ChildSessionTests.SessionPath(assignment);
        var changes = app.App.Services.GetRequiredService<LibraryChanges>();
        var family = (await parent.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("familyId").GetGuid();
        var stream = changes.Subscribe(family)!;
        Assert.True(stream.Reader.TryRead(out _));
        try
        {
            await ChildSessionTests.Start(child, path);
            Assert.True(stream.Reader.TryRead(out _));
            using var save = await child.PutAsJsonAsync(path, new { expectedRevision = 1, answers = content.Answers() });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            Assert.True(stream.Reader.TryRead(out _));
            using var conflict = await child.PutAsJsonAsync(path, new { expectedRevision = 1, answers = content.Answers() });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.False(stream.Reader.TryRead(out _));
            using var submit = await child.PostAsJsonAsync(path + "/submit", new { expectedRevision = 2, answers = content.Answers() });
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
            Assert.True(stream.Reader.TryRead(out _));
            Assert.Equal("awaiting-review", (await parent.GetFromJsonAsync<JsonNode>($"/api/assignments/{assignment["id"]}"))!["assignment"]!["status"]!.GetValue<string>());
        }
        finally { changes.Unsubscribe(family, stream); }
    }

    [Fact]
    public async Task Child_stream_notifies_committed_assignments_and_withdrawals_without_content()
    {
        await using var app = new ChildHarness();
        using var parent = await app.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        using var child = await app.Activate(parent, profile);
        var snapshot = await AssignmentTests.Snapshot(parent);
        using var stream = await ChangeStream.OpenAsync(child, "/api/child/changes");
        var assignment = await AssignmentTests.Assign(parent, profile, snapshot);
        await stream.NextAsync();
        var inbox = (await child.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!;
        Assert.Equal(assignment["id"]!.GetValue<Guid>(), inbox["items"]![0]!["id"]!.GetValue<Guid>());
        using var withdrawn = await parent.PostAsJsonAsync($"/api/assignments/{assignment["id"]}/withdraw", new { expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        await stream.NextAsync();
        Assert.Empty((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!["items"]!.AsArray());
    }

    [Fact]
    public async Task Child_stream_requires_a_valid_child_grant_and_does_not_open_parent_access()
    {
        await using var app = new ChildHarness();
        using var parent = await app.App.ParentAsync();
        using var anonymous = app.App.CreateClient();
        var profile = await ChildHarness.Create(parent);
        using var child = await app.Activate(parent, profile);
        foreach (var client in new[] { parent, anonymous })
        {
            using var denied = await client.GetAsync("/api/child/changes", HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        using (var denied = await child.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (await ChangeStream.OpenAsync(child, "/api/child/changes")) { }
        app.Clock.Now = app.Clock.Now.AddDays(31);
        using var expired = await child.GetAsync("/api/child/changes", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claim_and_checkpoint_notify_even_when_cancellation_precedes_the_provider_response(bool cancel)
    {
        await using var app = new GenerationHarness(GenerationHarness.Questions());
        using var owner = await app.ParentAsync();
        var draft = await Create(owner, Numeric(1));
        var operation = await GenerationHarness.Start(owner, draft);
        var changes = app.App.Services.GetRequiredService<LibraryChanges>();
        var familyId = (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("familyId").GetGuid();
        var stream = changes.Subscribe(familyId)!;
        Assert.True(stream.Reader.TryRead(out _));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Chat.BeforeResponse = async _ => { entered.SetResult(); await resume.Task; };
        var work = app.Worker.RunNextAsync(default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(stream.Reader.TryRead(out _));
            Assert.Equal("calling", (await owner.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!["status"]!.GetValue<string>());
            if (cancel)
            {
                using var response = await owner.PostAsync(GenerationHarness.OperationPath(operation) + "/cancel", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(stream.Reader.TryRead(out _));
            }
            resume.TrySetResult();
            await work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(stream.Reader.TryRead(out _));
            Assert.Equal(cancel ? "cancelled" : "completed",
                (await owner.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!["status"]!.GetValue<string>());
        }
        finally
        {
            resume.TrySetResult();
            await work.WaitAsync(TimeSpan.FromSeconds(10));
            changes.Unsubscribe(familyId, stream);
        }
    }

    [Fact]
    public void A_slow_reader_keeps_one_pending_note_and_other_families_receive_nothing()
    {
        var changes = new LibraryChanges();
        var family = Guid.NewGuid();
        var stream = changes.Subscribe(family)!;
        Assert.True(stream.Reader.TryRead(out _));
        changes.Publish(Guid.NewGuid());
        Assert.False(stream.Reader.TryRead(out _));
        for (var i = 0; i < 100; i++) changes.Publish(family);
        Assert.True(stream.Reader.TryRead(out _));
        Assert.False(stream.Reader.TryRead(out _));
        changes.Unsubscribe(family, stream);
    }

    [Fact]
    public async Task Library_write_routes_publish_only_after_success()
    {
        await using var app = new GenerationHarness();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var changes = app.App.Services.GetRequiredService<LibraryChanges>();
        var family = (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("familyId").GetGuid();
        var otherFamily = (await stranger.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("familyId").GetGuid();
        var stream = changes.Subscribe(family)!;
        var other = changes.Subscribe(otherFamily)!;
        Assert.True(stream.Reader.TryRead(out _));
        Assert.True(other.Reader.TryRead(out _));
        try
        {
            var draft = await Create(owner, Numeric(1));
            Notified();
            var edit = Edit(draft);
            edit["document"] = Document();
            draft = await Seed(owner, draft, edit);
            edit = Edit(draft);
            edit["document"]!["title"] = "שם שנערך";
            draft = await Save(owner, draft, edit);
            Notified();
            using (var conflict = await owner.PutAsJsonAsync(Path(draft), edit))
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using (var denied = await stranger.DeleteAsync(Path(draft)))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.False(stream.Reader.TryRead(out _));
            Assert.False(other.Reader.TryRead(out _));

            var operation = await GenerationHarness.Start(owner, draft, "GenerateQuestions");
            Notified();
            using (var cancel = await owner.PostAsync(GenerationHarness.OperationPath(operation) + "/cancel", null))
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Notified();
            draft = (await owner.GetFromJsonAsync<JsonNode>(Path(draft)))!;
            using var release = await owner.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() });
            Assert.Equal(HttpStatusCode.Created, release.StatusCode);
            var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
            Notified();
            using (var deleted = await owner.DeleteAsync($"/api/instances/{snapshot["id"]!.GetValue<Guid>()}"))
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Notified();
            using (var deleted = await owner.DeleteAsync(Path(draft))) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Notified();

            await Create(owner, Numeric(1));
            Notified();
            using (var reset = await owner.DeleteAsync("/api/learning-data")) Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Notified();
        }
        finally
        {
            changes.Unsubscribe(family, stream);
            changes.Unsubscribe(otherFamily, other);
        }

        void Notified()
        {
            Assert.True(stream.Reader.TryRead(out _));
            Assert.False(other.Reader.TryRead(out _));
        }
    }

    [Fact]
    public async Task Committed_writes_notify_only_the_writing_family()
    {
        await using var app = new GenerationHarness(GenerationHarness.Questions());
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await Create(owner, Numeric(1));
        await GenerationHarness.Start(owner, draft);
        var changes = app.App.Services.GetRequiredService<LibraryChanges>();
        var familyId = (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("familyId").GetGuid();
        var stream = changes.Subscribe(familyId)!;
        Assert.True(stream.Reader.TryRead(out _));

        await Create(stranger, Numeric(1));
        using (var missing = await owner.DeleteAsync($"/api/instances/{Guid.NewGuid()}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // A write publishes before its response, so a wrong note would already be pending.
        Assert.False(stream.Reader.TryRead(out _));
        await Create(owner, Numeric(1));
        Assert.True(stream.Reader.TryRead(out _));
        Assert.True(await app.Worker.RunNextAsync(default));
        Assert.True(stream.Reader.TryRead(out _));
        changes.Unsubscribe(familyId, stream);
    }

    [Fact]
    public async Task Streams_send_notes_and_are_bounded_per_family()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var streams = new List<ChangeStream>();
        for (var index = 0; index < LibraryChanges.FamilyStreamLimit; index++) streams.Add(await ChangeStream.OpenAsync(owner));
        await Create(owner, Numeric(1));
        await streams[0].NextAsync();
        using (var refused = await owner.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead))
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        using (await ChangeStream.OpenAsync(stranger)) { }
        streams[0].Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            // The server releases a slot once it observes the disconnect.
            using var reopened = await owner.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (reopened.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(50, timeout.Token);
        }
        streams.ForEach(stream => stream.Dispose());
    }

    private sealed class ChangeStream(HttpResponseMessage response, StreamReader reader) : IDisposable
    {
        /// <summary>Opens a stream and reads the note every new stream starts with.</summary>
        internal static async Task<ChangeStream> OpenAsync(HttpClient client, string path = StreamPath)
        {
            var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            var stream = new ChangeStream(response, new StreamReader(await response.Content.ReadAsStreamAsync()));
            await stream.NextAsync();
            return stream;
        }

        internal async Task NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Equal("data: changed", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("", await reader.ReadLineAsync(timeout.Token));
        }

        public void Dispose()
        {
            reader.Dispose();
            response.Dispose();
        }
    }
}
