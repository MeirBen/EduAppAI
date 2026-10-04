using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Features.Library;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LibraryChangeTests
{
    private const string StreamPath = "/api/library/changes";

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
