using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Shared choices for every subject; difficulty is easy, medium or hard relative to the audience.</summary>
public sealed record TaskSettings(
    [property: JsonRequired] string Topic,
    [property: JsonRequired] string Audience,
    [property: JsonRequired] string Difficulty,
    [property: JsonRequired] int QuestionCount);
