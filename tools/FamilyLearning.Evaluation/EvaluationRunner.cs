using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Immutable execution measurements for console and local dashboard consumers.</summary>
public sealed record EvaluationProgress(string Stage, string? CaseId, int? Repetition, int CompletedCalls,
    int PlannedCalls, string Status, string? Model, decimal? ReportedCostCredits, int MissingCostCalls);

/// <summary>Exercises the app's AI engine and saves evaluation artifacts without application identity or learning data.</summary>
public static class EvaluationRunner
{
    /// <summary>Runs sequentially with at most three 429 retries per stage, within the total call budget.</summary>
    public static async Task RunAsync(IChatClient client, AiGenerationOptions options, EvaluationReport report,
        string directory, CancellationToken ct, Action<EvaluationProgress>? progress = null, TimeProvider? timeProvider = null)
    {
        var capture = new EvaluationCapture(client, report.MaxCalls);
        using var engine = new AiGenerationService([capture], NullLogger<AiGenerationService>.Instance, Options.Create(options));
        string stage = "starting";
        string? caseId = null;
        int? currentRepetition = null;
        await SaveAsync();
        try
        {
            if (report.JudgeEnabled)
            {
                foreach (var sample in report.CalibrationSamples)
                {
                    ct.ThrowIfCancellationRequested();
                    stage = "calibration";
                    caseId = sample.Id;
                    var calibration = new CalibrationResult(sample, new());
                    report.Calibration.Add(calibration);
                    var review = await AttemptAsync(step => calibration.Call = step, token => HebrewJudge.ReviewAsync(
                        capture, sample.Request, sample.Texts, options.MaxOutputTokens, token));
                    calibration.Issues = review?.Issues;
                    await SaveAsync();
                }
            }
            foreach (var scenario in report.Cases)
            {
                for (var repetition = 1; repetition <= report.Repeat; repetition++)
                {
                    ct.ThrowIfCancellationRequested();
                    stage = "authoring";
                    caseId = scenario.Id;
                    currentRepetition = repetition;
                    var result = new EvaluationResult(scenario.Id, repetition);
                    report.Results.Add(result);
                    var definition = await AttemptAsync(step => result.Authoring = step, token => engine.AuthorAsync(scenario.Prompt, token));
                    await SaveAsync();
                    if (definition is null) continue;

                    // Presence is necessary, not proof of correct use. Match complete, case-sensitive ASCII identifiers.
                    var references = Regex.Matches(definition.Generation.Instructions, "[A-Za-z0-9_]+")
                        .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
                    result.Checks["parameterReferences"] = definition.InstanceParameters.All(parameter => references.Contains(parameter.Key));
                    var parameters = ParameterValidator.Validate(definition.InstanceParameters, new Dictionary<string, JsonElement>());
                    var questionCount = scenario.QuestionCountOverride ?? definition.Generation.QuestionCount;
                    result.Checks["parameterDefaults"] = parameters.Errors.Count == 0;
                    if (parameters.Errors.Count > 0) continue;
                    result.Parameters = parameters.Values;
                    stage = "generation";
                    var content = await AttemptAsync(step => result.Generation = step, token => engine.GenerateAsync(definition, parameters.Values, questionCount, token));
                    if (content is not null) CheckContent(scenario, content, result);
                    await SaveAsync();
                    if (content is not null && report.JudgeEnabled)
                    {
                        stage = "review";
                        var review = await AttemptAsync(step => result.Judge = step, token => HebrewJudge.ReviewAsync(capture,
                            scenario.Prompt, HebrewJudge.CollectTexts(definition, content), options.MaxOutputTokens, token));
                        result.Issues = review?.Issues;
                        await SaveAsync();
                    }
                }
            }
            report.Status = "completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { report.Status = "cancelled"; }
        catch (AiGenerationException exception) when (exception.StatusCode == 429) { report.Status = "rate-limited"; }
        catch (EvaluationCallLimitException) { report.Status = "call-limit"; }
        catch { report.Status = "failed"; throw; }
        finally
        {
            report.FinishedAtUtc = DateTime.UtcNow;
            await SaveAsync();
        }

        async Task<T?> AttemptAsync<T>(Action<EvaluationStep> setStep, Func<CancellationToken, Task<AiResult<T>>> operation) where T : class
        {
            var step = new EvaluationStep();
            setStep(step);
            EvaluationRetry? previousAttempt = null;
            var delay = report.AttemptedCalls > 0 ? report.CallDelaySeconds : 0d;
            for (var retry = 0; ; retry++)
            {
                // Scheduling waits never consume a request deadline or inflate provider latency.
                if (delay > 0)
                {
                    PublishProgress(retry == 0 ? "waiting" : "retry-wait");
                    await Task.Delay(TimeSpan.FromSeconds(delay), timeProvider ?? TimeProvider.System, ct);
                }
                ct.ThrowIfCancellationRequested();
                if (previousAttempt is not null)
                {
                    report.Retries.Add(previousAttempt);
                    step = new();
                    setStep(step);
                }
                try { return await AttemptOnceAsync(step, operation); }
                catch (OperationCanceledException) when (!step.RequestSent && previousAttempt is not null)
                {
                    // Cancellation before sending must retain the last real attempt as the stage's result.
                    report.Retries.Remove(previousAttempt);
                    setStep(previousAttempt.Call);
                    throw;
                }
                catch (AiGenerationException exception) when (exception.StatusCode == 429 && step.RequestSent)
                {
                    if (retry == 3) throw;
                    if (report.AttemptedCalls >= report.MaxCalls) throw new EvaluationCallLimitException();
                    delay = Math.Max(report.CallDelaySeconds,
                        step.RetryAfterSeconds ?? 5 * Math.Pow(2, retry) * (1 + Random.Shared.NextDouble() * 0.2));
                    // Never shorten a provider's long retry hint to fit a local waiting limit.
                    if (delay > 300) throw;
                    previousAttempt = new(stage, caseId, currentRepetition, retry + 1, delay, step);
                    await SaveAsync();
                }
            }
        }

        async Task<T?> AttemptOnceAsync<T>(EvaluationStep step, Func<CancellationToken, Task<AiResult<T>>> operation) where T : class
        {
            step.StartedAtUtc = DateTime.UtcNow;
            capture.Current = step;
            PublishProgress();
            var started = Stopwatch.GetTimestamp();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(options.RequestTimeout);
            try
            {
                var generated = await operation(deadline.Token);
                step.Metadata = generated.Metadata;
                step.ContractValid = true;
                return generated.Value;
            }
            catch (AiGenerationException exception)
            {
                step.StatusCode = exception.StatusCode;
                step.Failure = exception.ProblemType ?? exception.Message;
                step.ValidationErrors = exception.ValidationErrors;
                if (exception.StatusCode == 429) throw;
                return null;
            }
            catch (EvaluationCallLimitException) { step.Failure = "call-limit"; throw; }
            catch (OperationCanceledException)
            {
                step.Failure = ct.IsCancellationRequested ? "cancelled" : "timeout";
                if (ct.IsCancellationRequested) throw;
                step.StatusCode = 504;
                return null;
            }
            catch (JsonException) { step.Failure = "invalid-review-json"; return null; }
            catch (Exception exception) when (exception is HttpRequestException or ClientResultException)
            {
                step.StatusCode = exception is ClientResultException response ? response.Status :
                    (int?)((HttpRequestException)exception).StatusCode;
                step.Failure = "provider-error";
                if (step.StatusCode == 429) throw new AiGenerationException(429, "rate-limited");
                return null;
            }
            finally
            {
                step.ElapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                step.FinishedAtUtc = DateTime.UtcNow;
            }
        }

        async Task SaveAsync()
        {
            await EvaluationFiles.SaveAsync(report, directory);
            PublishProgress();
        }

        void PublishProgress(string? currentStage = null) => progress?.Invoke(new(currentStage ?? stage, caseId, currentRepetition,
            report.Steps.Count(step => step.FinishedAtUtc.HasValue && step.RequestSent), report.PlannedCalls + report.Retries.Count, report.Status,
            report.Steps.LastOrDefault(step => step.Model is not null)?.Model, report.ReportedCostCredits,
            report.Steps.Count(step => step.RequestSent && step.FinishedAtUtc.HasValue && !step.CostCredits.HasValue)));
    }

    // Evaluation-only adherence metric: whitespace-delimited body words, excluding an exact leading task title.
    private static int CountPassageWords(TaskContent content)
    {
        var words = 0;
        var title = content.Title?.Trim();
        for (var i = 0; i < content.ContentBlocks.Length; i++)
        {
            var text = content.ContentBlocks[i].Text.Trim();
            if (i == 0)
            {
                var newline = text.IndexOf('\n');
                if (newline >= 0 && text[..newline].Trim() == title) text = text[(newline + 1)..];
                else if (text == title) text = "";
            }
            words += text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }
        return words;
    }

    private static void CheckContent(EvaluationCase scenario, TaskContent content, EvaluationResult result)
    {
        var checks = result.Checks;
        checks["questionCount"] = content.Questions.Length == scenario.QuestionCount;
        checks["interaction"] = content.Questions.All(question => question.Interaction.Type == scenario.Interaction);
        if (scenario.ChoiceCount is { } count)
            checks["choiceCount"] = content.Questions.All(question => question.Interaction.Options?.Length == count);
        if (scenario.MaxPassageWords == 0)
            checks["noPassage"] = content.ContentBlocks.Length == 0;
        else if (scenario.MinPassageWords.HasValue || scenario.MaxPassageWords.HasValue)
        {
            result.PassageWordCount = CountPassageWords(content);
            checks["passageLength"] = (!scenario.MinPassageWords.HasValue || result.PassageWordCount >= scenario.MinPassageWords) &&
                (!scenario.MaxPassageWords.HasValue || result.PassageWordCount <= scenario.MaxPassageWords);
        }
        var positions = content.Questions.Where(question => question.Interaction.Type == "single-choice")
            .Select(question => Array.IndexOf(question.Interaction.Options!, question.Answer.Value) + 1).ToArray();
        if (positions.Length >= 3 && positions[0] > 0 && positions.All(position => position == positions[0]))
            result.RepeatedAnswerPosition = positions[0];
    }
}
