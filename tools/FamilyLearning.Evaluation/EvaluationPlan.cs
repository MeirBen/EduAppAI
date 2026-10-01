
namespace FamilyLearning.Evaluation;

/// <summary>Developer choices only. Confirmation authorizes a live UI run; preview never uses it.</summary>
public sealed record EvaluationRunRequest(string[] CaseIds, int Repeat, bool Judge, int MaxCalls,
    string? Label = null, string? RunNotes = null, bool Confirmed = false, int CallDelaySeconds = 5);

/// <summary>Validated fixtures in their original order, shared by CLI and dashboard before provider use.</summary>
public sealed record EvaluationPlan(EvaluationRunRequest Request, EvaluationCase[] Cases, string SuiteSha256,
    CalibrationSample[] Controls, string CalibrationSha256)
{
    public int PlannedCalls => Cases.Sum(scenario => scenario.PlannedCalls + (Request.Judge ? 1 : 0)) * Request.Repeat + Controls.Length;

    public static async Task<EvaluationPlan> LoadAsync(EvaluationRunRequest request, string? fixtureDirectory = null)
    {
        if (request.CaseIds is not { Length: > 0 and <= 100 } || request.Repeat is < 1 or > 5 || request.MaxCalls is < 1 or > 100 ||
            request.CallDelaySeconds is < 0 or > 60 ||
            request.CaseIds.Any(string.IsNullOrWhiteSpace) || request.CaseIds.Distinct().Count() != request.CaseIds.Length)
            throw new ArgumentException("Select cases, 1–5 repeats, a call limit of 1–100 and a call delay of 0–60 seconds.");
        EvaluationFiles.ValidateRunMetadata(request.Label, request.RunNotes);
        var suite = await EvaluationFiles.LoadFixtureAsync<EvaluationCase>("cases.json", fixtureDirectory);
        var all = request.CaseIds is ["all"];
        var cases = suite.Items.Where(item => all || request.CaseIds.Contains(item.Id, StringComparer.Ordinal)).ToArray();
        if (cases.Length == 0) throw new ArgumentException("No cases match the selected suite.");
        if (!all && cases.Length != request.CaseIds.Length) throw new ArgumentException("Unknown evaluation case.");
        var controls = request.Judge
            ? await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json", fixtureDirectory)
            : (Items: Array.Empty<CalibrationSample>(), Sha256: "");
        return new(request, cases, suite.Sha256, controls.Items, controls.Sha256);
    }

    public void ValidateBudget()
    {
        if (PlannedCalls > Request.MaxCalls) throw new ArgumentException("Planned calls exceed the call limit.");
    }

    public EvaluationReport CreateReport(Dictionary<string, string?> profile) => new(Cases, Request.Repeat, SuiteSha256, profile)
    {
        JudgeEnabled = Request.Judge,
        MaxCalls = Request.MaxCalls,
        CallDelaySeconds = Request.CallDelaySeconds,
        Label = Request.Label,
        RunNotes = Request.RunNotes,
        CalibrationSha256 = CalibrationSha256,
        CalibrationSamples = Controls,
        JudgePromptVersion = Request.Judge ? HebrewJudge.Version : "",
        JudgePrompt = Request.Judge ? HebrewJudge.Instructions : ""
    };

}
