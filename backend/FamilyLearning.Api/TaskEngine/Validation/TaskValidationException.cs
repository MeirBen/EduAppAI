namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>A strict engine boundary rejected content. Errors contain safe field feedback, never raw provider text.</summary>
public sealed class TaskValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("תוכן הפעילות אינו עומד בדרישות.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
