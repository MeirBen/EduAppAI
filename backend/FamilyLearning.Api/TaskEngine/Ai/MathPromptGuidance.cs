namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Math interpretation within generic plan authoring, including mixed-subject activities.</summary>
internal static class MathPromptGuidance
{
    internal const string Planning = """
        For math, preserve operations, number domains, precision and bounds by role: factors/product and dividend/divisor/quotient.
        Grade and difficulty guide selection, not unrequested hard operand limits or claims about curriculum requirements.
        For elementary "up to N" with no scope, propose given numbers and results within N and disclose this assumption; preserve explicit scopes.
        Preserve requested fractions and remainders. Choose a compatible answer format; clarify conflicting requirements instead of silently simplifying.
        """;
}
