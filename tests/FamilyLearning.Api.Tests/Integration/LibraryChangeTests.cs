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

            var operation = await GenerationHarness.Start(owner, draft);
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

            using var published = await owner.PostAsJsonAsync("/api/templates", Numeric(1));
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);
            var template = (await published.Content.ReadFromJsonAsync<JsonNode>())!;
            var templatePath = $"/api/templates/{template["id"]!.GetValue<Guid>()}";
            Notified();
            using (var version = await owner.PostAsJsonAsync(templatePath + "/versions", new { expectedVersion = 1, definition = Numeric(1) }))
                Assert.True(version.IsSuccessStatusCode);
            Notified();
            using (var deleted = await owner.DeleteAsync(templatePath)) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
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

        using (await stranger.PostAsJsonAsync("/api/templates", Numeric(1))) { }
        using (var missing = await owner.DeleteAsync($"/api/instances/{Guid.NewGuid()}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // A write publishes before its response, so a wrong note would already be pending.
        Assert.False(stream.Reader.TryRead(out _));
        using (await owner.PostAsJsonAsync("/api/templates", Numeric(1))) { }
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
        using (await owner.PostAsJsonAsync("/api/templates", Numeric(1))) { }
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
        internal static async Task<ChangeStream> OpenAsync(HttpClient parent)
        {
            var response = await parent.GetAsync(StreamPath, HttpCompletionOption.ResponseHeadersRead);
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
