using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Evaluation;

/// <summary>Independent fixture adherence; engine validity alone cannot detect a dropped parent requirement.</summary>
internal static class EvaluationChecks
{
    internal static void Plan(EvaluationCase scenario, EvaluationResult result)
    {
        var input = result.Input!;
        var plan = result.Plan!;
        result.Checks["planQuestionCount"] = input.Settings.QuestionCount == scenario.QuestionCount;
        result.Checks["planInteraction"] = input.Questions.Formats.Contains(scenario.Interaction);
        if (scenario.ChoiceCount is { } choices) result.Checks["planChoiceCount"] = input.Questions.ChoiceCount == choices;
        if (scenario.InitialPlan is null)
            result.Checks["planGeneratedMaterials"] = input.Materials.Count(material => material.Source == "generated") == scenario.ExpectedGeneratedMaterials;
        if (scenario.ExpectedLength is { } length)
            result.Checks["planLength"] = input.Materials.Count(material => material.Source == "generated") > 1
                ? input.TotalLength == length : input.Materials.Any(material => material.Length == length);
        if (scenario.AdditionalControlCount is { } count)
            result.Checks["additionalControlCount"] = plan.Controls.Length + plan.Questions.Controls.Length +
                plan.Materials.Sum(material => material.Controls.Length) == count;
    }

    internal static void Content(EvaluationCase scenario, EvaluationResult result)
    {
        var document = result.Document!;
        var checks = result.Checks;
        checks["questionCount"] = document.Questions.Length == scenario.QuestionCount;
        checks["interaction"] = document.Questions.All(question => question.Interaction.Type == scenario.Interaction);
        if (scenario.ChoiceCount is { } choices)
            checks["choiceCount"] = document.Questions.All(question => question.Interaction.Options?.Length == choices);
        if (scenario.MaxPassageWords == 0) checks["noPassage"] = document.Materials.Length == 0;
        else if (scenario.MinPassageWords.HasValue || scenario.MaxPassageWords.HasValue)
        {
            result.PassageWordCount = document.Materials.Sum(material => TextLength.CountWords(material.Body));
            checks["passageLength"] = (!scenario.MinPassageWords.HasValue || result.PassageWordCount >= scenario.MinPassageWords) &&
                (!scenario.MaxPassageWords.HasValue || result.PassageWordCount <= scenario.MaxPassageWords);
        }
        result.Measurements = TextLength.Measure(result.Input!, document);
        // Bidi mirroring reverses < and > beside Hebrew words (ui-guide rendering contract); whole-item expressions display in order.
        checks["signDirection"] = LearnerTexts(document).All(text => text.AsSpan().IndexOfAny('<', '>') < 0 || !text.Any(IsHebrewLetter));
        var calculations = 0;
        var mismatches = 0;
        foreach (var question in document.Questions)
        {
            if (!ExactArithmetic.TryEvaluate(question.Prompt, out var expected)) continue;
            calculations++;
            // A key in another form, such as a quotient with a remainder, is outside this check; validators own key shape.
            if (ExactArithmetic.TryEvaluate(question.Answer?.Value, out var key) && key != expected) mismatches++;
        }
        if (calculations > 0) checks["calculationKeys"] = mismatches == 0;
        var positions = document.Questions.Where(question => question.Interaction.Type == "single-choice")
            .Select(question => Array.IndexOf(question.Interaction.Options!, question.Answer!.Value) + 1).ToArray();
        if (positions.Length >= 3 && positions[0] > 0 && positions.All(position => position == positions[0]))
            result.RepeatedAnswerPosition = positions[0];
    }

    private static IEnumerable<string> LearnerTexts(TaskDocument document)
    {
        yield return document.Title;
        if (document.Instructions is { } instructions) yield return instructions;
        foreach (var material in document.Materials)
        {
            if (material.Title is { } title) yield return title;
            yield return material.Body;
        }
        foreach (var question in document.Questions)
        {
            yield return question.Prompt;
            foreach (var option in question.Interaction.Options ?? []) yield return option;
            if (question.Answer is { } answer) yield return answer.Value;
        }
    }

    private static bool IsHebrewLetter(char character) => character is >= '\u05D0' and <= '\u05EA';
}
