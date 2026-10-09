using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>One parent-initiated proposal call. Client correlation fields are never sent to the provider.</summary>
public sealed record ActivityAuthoringInput(string Message, LearningPlan? BaseDefinition = null,
    AuthoringTurn[]? Context = null, string? RequestId = null, long? BaseRevision = null);

/// <summary>Unresolved conversation only; roles are parent or assistant. Accepted requirements live in the base plan.</summary>
public sealed record AuthoringTurn(string Role, string Text);

/// <summary>A validated proposal or one clarification, with application-computed changes.</summary>
public sealed record AuthoringReply(LearningPlan? Proposal, string? Clarification, string[] Assumptions, PlanChange[] Changes);

// Provider replies cannot supply the application's computed change list or correlation metadata.
internal sealed record AuthoringCandidate([property: JsonRequired] AuthoringDecision Result,
    [property: JsonRequired] string[] Assumptions);

// A nested union keeps the provider schema's root a strict object; the public reply stays flat.
internal sealed record AuthoringDecision([property: JsonRequired] LearningPlan? Proposal,
    [property: JsonRequired] string? Clarification);
