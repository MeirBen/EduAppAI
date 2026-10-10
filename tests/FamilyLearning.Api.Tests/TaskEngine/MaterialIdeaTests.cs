using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class MaterialIdeaTests
{
    [Fact]
    public void Draw_breaks_ties_among_the_lowest_overlap_only()
    {
        var ideas = Ideas();
        ideas.Ideas[1] = ideas.Ideas[1] with { RecentOverlap = 20 };
        ideas.Ideas[3] = ideas.Ideas[3] with { RecentOverlap = 20 };
        Assert.Equal(ideas.Ideas[1].Idea, MaterialIdeas.Select(ideas, 0));
        Assert.Equal(ideas.Ideas[3].Idea, MaterialIdeas.Select(ideas, 1));
        Assert.Equal(ideas.Ideas[3].Idea, MaterialIdeas.Select(ideas, -1));
    }

    [Fact]
    public async Task Ideas_are_scored_against_history_and_the_writer_receives_only_the_selected_idea()
    {
        var ideas = Ideas();
        ideas.Ideas[3] = ideas.Ideas[3] with { RecentOverlap = 0 };
        using var chat = new AiFixtures.ScriptedChat(Serialize(ideas), Serialize(Materials()));
        var service = Service(chat);
        var request = Resolve(Reading());
        var input = TaskAssembly.PrepareMaterials(request, TaskAssembly.CreateDocument(request))!;
        var proposals = await service.GenerateMaterialIdeasAsync(input, [new("previous premise", "previous structure")], default);
        var selected = MaterialIdeas.Select(proposals.Value, 0);
        Assert.Equal(ideas.Ideas[3].Idea, selected);
        var content = await service.GenerateMaterialsAsync(input, selected, default);
        var accepted = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), content.Value, content.Metadata, selected);
        Assert.Equal(selected, Assert.Single(accepted.Document!.Materials).Idea);
        using var proposing = JsonDocument.Parse(chat.Requests[0].Input.Split('\n')[^1]);
        Assert.Equal("previous structure", proposing.RootElement.GetProperty("history")[0].GetProperty("structure").GetString());
        using var writing = JsonDocument.Parse(chat.Requests[1].Input.Split('\n')[^1]);
        Assert.Equal(selected.Premise, writing.RootElement.GetProperty("idea").GetProperty("premise").GetString());
        Assert.False(writing.RootElement.TryGetProperty("history", out _));
        Assert.Equal(2, chat.Requests.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("score")]
    [InlineData("long")]
    [InlineData("blank")]
    public async Task Invalid_ideas_fail_without_a_writer_call(string defect)
    {
        var ideas = Ideas();
        ideas = defect switch
        {
            "missing" => ideas with { Ideas = ideas.Ideas[..4] },
            "duplicate" => ideas with { Ideas = [ideas.Ideas[0], .. ideas.Ideas[..4]] },
            "score" => ideas with { Ideas = [ideas.Ideas[0] with { RecentOverlap = -1 }, .. ideas.Ideas[1..]] },
            "long" => ideas with { Ideas = [ideas.Ideas[0] with { Idea = new(new string('א', 401), "structure") }, .. ideas.Ideas[1..]] },
            _ => ideas with { Ideas = [ideas.Ideas[0] with { Idea = new("premise", " ") }, .. ideas.Ideas[1..]] }
        };
        using var chat = new AiFixtures.ScriptedChat(Serialize(ideas));
        var service = Service(chat);
        var request = Resolve(Reading());
        await Assert.ThrowsAsync<AiGenerationException>(() => service.GenerateMaterialIdeasAsync(
            TaskAssembly.PrepareMaterials(request, TaskAssembly.CreateDocument(request))!, [], default));
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("null-batch")]
    [InlineData("null-candidate")]
    [InlineData("null-idea")]
    [InlineData("unknown-field")]
    public async Task Malformed_idea_output_is_rejected(string defect)
    {
        var json = JsonNode.Parse(Serialize(Ideas()))!;
        switch (defect)
        {
            case "null-batch": json["ideas"] = null; break;
            case "null-candidate": json["ideas"]![0] = null; break;
            case "null-idea": json["ideas"]![0]!["idea"] = null; break;
            default: json["ideas"]![0]!["idea"]!["body"] = "unrequested material"; break;
        }
        using var chat = new AiFixtures.ScriptedChat(json.ToJsonString());
        var service = Service(chat);
        var request = Resolve(Reading());
        await Assert.ThrowsAsync<AiGenerationException>(() => service.GenerateMaterialIdeasAsync(
            TaskAssembly.PrepareMaterials(request, TaskAssembly.CreateDocument(request))!, [], default));
        Assert.Single(chat.Requests);
    }

    internal static MaterialIdeaCandidateBatch Ideas() => new(Enumerable.Range(1, 5)
        .Select(i => new MaterialIdeaCandidate(new($"premise {i}", $"structure {i}"), 50)).ToArray());
}
