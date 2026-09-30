using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Shared choices for every subject; difficulty is easy, medium or hard relative to the audience.</summary>
/// <remarks>Topic and audience are required text up to 200 characters; question count is a positive integer.</remarks>
public sealed record TaskSettings(
    [property: JsonRequired] string Topic,
    [property: JsonRequired] string Audience,
    [property: JsonRequired] string Difficulty,
    [property: JsonRequired] int QuestionCount);

/// <summary>Chosen settings and additional parameters; persist resolved values with the generated snapshot.</summary>
public sealed record TaskInput(
    [property: JsonRequired] TaskSettings Settings,
    [property: JsonRequired] Dictionary<string, JsonElement> Parameters);
