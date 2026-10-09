namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Either a complete effective request or field errors. A failed resolution never exposes partial values.</summary>
public sealed record TaskResolution(ResolvedTaskRequest? Value, Dictionary<string, string[]> Errors);

/// <summary>Effective requirements derived only from the saved plan. Treat collections as immutable after resolution.</summary>
public sealed record ResolvedTaskRequest(int SchemaVersion, int EngineRevision, string Goal, string Guidance,
    TaskSettings Settings, ResolvedMaterial[] Materials, ResolvedQuestions Questions,
    ResolvedLength? TotalLength);

/// <summary>One material's effective instructions and exact accepted source, where supplied.</summary>
public sealed record ResolvedMaterial(string Id, string Label, string Source, string Guidance, string? Text,
    ResolvedLength? Length);

/// <summary>Every listed format must occur in the complete question batch.</summary>
public sealed record ResolvedQuestions(string[] Formats, int? ChoiceCount, string Guidance);

/// <summary>A generated-word expectation: an advisory target Value, or a strict inclusive Lower–Upper range.</summary>
public sealed record ResolvedLength(string Mode, int? Value = null, int? Lower = null, int? Upper = null);
