using static FamilyLearning.Api.TaskEngine.Validation.EngineValidation;

namespace FamilyLearning.Api.TaskEngine;

/// <summary>
/// What an AI call sees of a conversation, before and after a draft exists: its newest turns, oldest first, up to
/// <c>MaxContextTurns</c> and <c>ContextLength</c>. The window ends at the first turn that would exceed the length;
/// older turns stay in the conversation but leave the context.
/// </summary>
internal static class ConversationWindow
{
    internal static T[] Latest<T>(IReadOnlyList<T> turns, Func<T, string> text)
    {
        var start = turns.Count;
        for (var length = 0; start > 0 && turns.Count - start < MaxContextTurns; start--)
        {
            length += text(turns[start - 1]).Length;
            if (length > ContextLength) break;
        }
        return [.. turns.Skip(start)];
    }
}
