namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Server-enforced limits published to the client, so its native validation, caps and copy never diverge from the API.</summary>
/// <remarks>Validators remain authoritative; this projection only reports their <see cref="EngineValidation"/> constants.</remarks>
public sealed record ContentLimits(
    int MaxQuestionCount, int MinChoiceCount, int MaxChoiceCount, int MaxMaterials,
    int MaxPoints, int NameLength, int GoalLength, int GuidanceLength, int ScopedGuidanceLength,
    int SettingTextLength, int TitleLength,
    int InstructionsLength, int BodyLength, int PromptLength, int AnswerLength, int ContentLength, int MessageLength,
    int MaxContextTurns, int ContextLength, int ListLimit, int MaxChildAge, int RevisionReplyLength, int EditInstructionLength, int MaxSelectedEdits, int MaxAssumptions, int AssumptionLength, int MaxChatTurns, int AuthoringReplyLength)
{
    public static ContentLimits Current { get; } = new(
        EngineValidation.MaxQuestionCount, EngineValidation.MinChoiceCount, EngineValidation.MaxChoiceCount,
        EngineValidation.MaxMaterials, EngineValidation.MaxPoints,
        EngineValidation.NameLength, EngineValidation.GoalLength, EngineValidation.GuidanceLength, EngineValidation.ScopedGuidanceLength,
        EngineValidation.SettingTextLength, EngineValidation.TitleLength,
        EngineValidation.InstructionsLength, EngineValidation.BodyLimit, EngineValidation.PromptLength, EngineValidation.AnswerLength,
        EngineValidation.ContentLimit, EngineValidation.MessageLength, EngineValidation.MaxContextTurns, EngineValidation.ContextLength,
        EngineValidation.ListLimit, EngineValidation.MaxChildAge, EngineValidation.RevisionReplyLength, EngineValidation.EditInstructionLength, EngineValidation.MaxSelectedEdits, EngineValidation.MaxAssumptions, EngineValidation.AssumptionLength, EngineValidation.MaxChatTurns, EngineValidation.AuthoringReplyLength);
}
