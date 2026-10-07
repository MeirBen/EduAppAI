using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ContentPolishTests
{
    private static readonly TaskDocument Empty = new("", null, [], []);

    [Fact]
    public async Task Material_polish_edits_generated_text_in_place_keeps_its_idea_and_never_sends_a_supplied_source()
    {
        var request = Resolve(Reading() with { Materials = [Reading().Materials[0], Supplied().Materials[0] with { Id = OtherId }] });
        var idea = new MaterialIdea("רעיון", "מבנה");
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), Materials(), idea: idea).Document!;
        using var chat = new AiFixtures.ScriptedChat(Serialize(new MaterialCandidateBatch([new(MaterialId, null, "שלום לכולם")])));
        using var service = Service(chat);
        var input = new PolishInput(request, document);
        var result = await service.PolishMaterialsAsync(input, default);
        var polished = TaskAssembly.PolishMaterials(input, result.Value, result.Metadata);
        Assert.Equal("שלום לכולם", polished.Materials[0].Body);
        Assert.Equal(document.Materials[0].Revision + 1, polished.Materials[0].Revision);
        Assert.Equal(idea, polished.Materials[0].Idea);
        Assert.Equal(result.Metadata, polished.Materials[0].Origin.Generation);
        Assert.Equal(Serialize(document.Materials[1]), Serialize(polished.Materials[1]));
        Assert.Null(TaskAssembly.PrepareMaterials(request, polished));
        using var sent = JsonDocument.Parse(chat.Requests[0].Input.Split('\n')[^1]);
        Assert.Equal(MaterialId, Assert.Single(sent.RootElement.GetProperty("materials").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal($"content-first-material-polish-v{EngineVersions.Revision}", result.Metadata.PromptVersion);
        // An unchanged polish keeps the revision, so nothing that depends on the text becomes stale.
        Assert.Equal(Serialize(document), Serialize(TaskAssembly.PolishMaterials(input, Materials())));
    }

    [Theory]
    [InlineData(OtherId, "סיפור חדש")]
    [InlineData(MaterialId, "קצר")]
    public async Task Material_polish_rejects_a_forged_ID_or_a_strict_length_failure(string id, string body)
    {
        var request = Resolve(Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 2, Upper: 3) }] });
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), new([new(MaterialId, null, "סיפור קצר מאוד")])).Document!;
        using var chat = new AiFixtures.ScriptedChat(Serialize(new MaterialCandidateBatch([new(id, null, body)])));
        using var service = Service(chat);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.PolishMaterialsAsync(new(request, document), default));
        Assert.Equal("validation", error.Category);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Material_polish_needs_current_generated_text_before_any_call()
    {
        using var chat = new AiFixtures.ScriptedChat(Serialize(Materials()));
        using var service = Service(chat);
        var numeric = Resolve(Numeric());
        await Assert.ThrowsAsync<TaskValidationException>(() => service.PolishMaterialsAsync(new(numeric, TaskAssembly.CreateDocument(numeric)), default));
        var strict = Resolve(Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] });
        var manual = new TaskDocument("", null, [new(MaterialId, 1, null, "קצר", new("manual"), new(TaskRequestResolver.Fingerprint(strict), []))], []);
        await Assert.ThrowsAsync<TaskValidationException>(() => service.PolishMaterialsAsync(new(strict, manual), default));
        Assert.Empty(chat.Requests);
    }
}
