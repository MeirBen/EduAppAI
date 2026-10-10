using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Imported unsaved authoring conversation cannot claim an operation or an applied outcome.</summary>
public sealed record ImportedChatTurn([property: JsonRequired] string Role, [property: JsonRequired] string Text,
    [property: JsonRequired] DateTime AtUtc, RevisionTarget? Target = null, string[]? Assumptions = null);

/// <summary>
/// Parent-only conversation; operation references and outcomes are assigned by the server. A reply that
/// only summarizes applied changes stores them once as <see cref="Changes"/> with empty <see cref="Text"/>.
/// </summary>
public sealed record ActivityChatTurn(string Role, string Text, DateTime AtUtc, RevisionTarget? Target = null,
    Guid? OperationId = null, string[]? Assumptions = null, string? Outcome = null, string[]? Changes = null)
{
    /// <summary>The turn as conversation text: its message, or the change statements a summary lists.</summary>
    internal string Message => Text.Length == 0 && Changes is { } changes ? string.Join(" ", changes) : Text;
}

/// <summary>One prior content checkpoint, valid only at the revision produced by its successful operation.</summary>
public sealed record ActivityUndo(LearningPlan Plan, TaskDocument Document, long ResultingRevision);

internal static class ActivityChat
{
    internal static ActivityChatTurn[] Import(ImportedChatTurn[]? turns, LearningPlan plan)
    {
        if (turns is null) return [];
        if (turns.Length > MaxChatTurns || turns.Any(t => t is null || !IsTurn(t.Role, t.Text) || t.AtUtc.Kind != DateTimeKind.Utc ||
            t.Assumptions is { } assumptions && (t.Role != "assistant" || assumptions.Length > MaxAssumptions || assumptions.Any(a => !HasText(a, AssumptionLength))) ||
            t.Target is { } target && !(target.Kind == "material" && plan.Materials.Any(m => m.Id == target.Id))))
            throw new TaskValidationException("chat", "השיחה המיובאת אינה תקינה או ארוכה מדי.");
        return turns.Select(t => new ActivityChatTurn(t.Role, t.Text, t.AtUtc, t.Target, Assumptions: t.Assumptions)).ToArray();
    }

    internal static ActivityChatTurn[] Append(ActivityChatTurn[] current, ActivityChatTurn turn)
    {
        var turns = current.Append(turn).ToList();
        // Admission and completion are serialized with the operation. The sole pending exchange is always newest.
        while (turns.Count > MaxChatTurns)
        {
            var first = turns[0];
            turns.RemoveAt(0);
            if (first.Role == "parent")
                while (turns.Count > 0 && turns[0].Role == "assistant" && (first.OperationId is null || turns[0].OperationId == first.OperationId))
                    turns.RemoveAt(0);
        }
        return turns.ToArray();
    }

    internal static RevisionTurn[] Context(ActivityChatTurn[] chat) =>
        [.. ConversationWindow.Latest(chat, turn => turn.Message).Select(turn => new RevisionTurn(turn.Role, turn.Message, turn.Target, turn.Outcome))];
}
