using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NJsonSchema;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class AiSchemaTests
{
    [Theory]
    [InlineData("null", true)]
    [InlineData("{\"min\":100,\"max\":150}", true)]
    [InlineData("{\"min\":100,\"max\":null}", true)]
    [InlineData("{\"min\":null,\"max\":150}", true)]
    [InlineData("{\"min\":0,\"max\":0}", true)]
    [InlineData("{\"min\":4000,\"max\":4000}", true)]
    [InlineData("{\"min\":null,\"max\":null}", false)]
    [InlineData("{}", false)]
    [InlineData("{\"min\":100}", false)]
    [InlineData("{\"max\":150}", false)]
    [InlineData("{\"min\":-1,\"max\":150}", false)]
    [InlineData("{\"min\":null,\"max\":4001}", false)]
    [InlineData("{\"min\":100.5,\"max\":150}", false)]
    [InlineData("{\"min\":\"100\",\"max\":150}", false)]
    [InlineData("{\"min\":100,\"max\":150,\"extra\":true}", false)]
    public async Task Authoring_schema_allows_no_limit_or_at_least_one_numeric_bound(string range, bool valid)
    {
        var definition = JsonSerializer.SerializeToNode(new TaskTemplateDefinition(2, "Generic task", [],
            new("Create one question.")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var chat = new AiFixtures.ScriptedChat(definition.ToJsonString());
        using var service = new AiGenerationService([chat], NullLogger<AiGenerationService>.Instance,
            Options.Create(new AiGenerationOptions()));
        await service.AuthorAsync("A learning idea", CancellationToken.None);
        var format = Assert.IsType<ChatResponseFormatJson>(Assert.Single(chat.Requests).Options!.ResponseFormat);
        var schema = await JsonSchema.FromJsonAsync(JsonSerializer.Serialize(format.Schema));

        definition["generation"]!["contentWordCount"] = JsonNode.Parse(range);

        Assert.Equal(valid, schema.Validate(definition.ToJsonString()).Count == 0);
    }
}
