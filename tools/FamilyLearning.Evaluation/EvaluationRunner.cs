using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
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
                    var review = await AttemptAsync(step => calibration.Call = step, (evidence, token) => HebrewJudge.ReviewAsync(
                        capture, sample.Request, sample.Texts, options.MaxOutputTokens, token, evidence));
                    calibration.Issues = review?.Issues;
                    await SaveAsync();
                }
            }
            foreach (var scenario in report.Cases)
            {
                for (var repetition = 1; repetition <= report.Repeat; repetition++)
                {
                    ct.ThrowIfCancellationRequested();
                    caseId = scenario.Id;
                    currentRepetition = repetition;
                    var result = new EvaluationResult(scenario.Id, repetition)
                    {
                        Plan = scenario.InitialPlan,
                        Authoring = Skipped("authoring", scenario.InitialPlan is null ? "earlier-stage" : "fixed-plan"),
                        Materials = Skipped("materials", "earlier-stage"),
                        Generation = Skipped("questions", "earlier-stage"),
                        Judge = Skipped("review", report.JudgeEnabled ? "earlier-stage" : "disabled")
                    };
                    result.Refinements.AddRange(scenario.Refinements.Select(_ => Skipped("refinement", "earlier-stage")));
                    result.Replacements.AddRange(scenario.Replacements.Select(replacement => Skipped(replacement.Stage, "earlier-stage")));
                    report.Results.Add(result);
                    if (scenario.InitialPlan is null && !await AuthorPlanAsync(scenario, result)) continue;
                    var plan = result.Plan!;
                    var resolution = TaskRequestResolver.Resolve(plan, scenario.InitialInput ?? new(scenario.SettingsOverride ?? plan.Defaults));
                    result.Checks["inputResolution"] = resolution.Value is not null;
                    if (resolution.Value is not { } input)
                    {
                        result.InterpretationPassed = scenario.InitialPlan is null ? false : null;
                        await SaveAsync();
                        continue;
                    }
                    result.Input = input;
                    EvaluationChecks.Plan(scenario, result);
                    result.InterpretationPassed = scenario.InitialPlan is null ? result.Checks.Values.All(value => value) : null;
                    result.Document = TaskAssembly.CreateDocument(input);
                    if (TaskAssembly.PrepareMaterials(input, result.Document) is { } materials)
                    {
                        stage = "materials";
                        var generated = await AttemptAsync(step => result.Materials = step,
                            (evidence, token) => engine.GenerateMaterialsAsync(materials, token, evidence), input, materials.Materials);
                        if (generated is null) { await SaveAsync(); continue; }
                        var accepted = TaskAssembly.AcceptMaterials(input, result.Document, generated, result.Materials!.Metadata);
                        if (accepted.Document is null)
                        {
                            Reject(result.Materials, accepted.Diagnostics);
                            await SaveAsync();
                            continue;
                        }
                        result.Document = accepted.Document;
                        result.Materials.Applied = true;
                        await SaveAsync();
                    }
                    else result.Materials = Skipped("materials", "no-generated-materials");
                    stage = "questions";
                    var questions = TaskAssembly.PrepareQuestions(input, result.Document);
                    var content = await AttemptAsync(step => result.Generation = step,
                        (evidence, token) => engine.GenerateQuestionsAsync(questions, token, evidence), input, questions.Materials);
                    if (content is null) { await SaveAsync(); continue; }
                    result.Document = TaskAssembly.AcceptQuestions(input, result.Document, content, result.Generation!.Metadata);
                    result.Generation.Applied = true;
                    result.GenerationPassed = TaskDocumentValidator.ValidateRelease(input, result.Document).Count == 0;
                    for (var index = 0; index < scenario.Replacements.Length; index++)
                    {
                        var replacement = scenario.Replacements[index];
                        stage = replacement.Stage;
                        if (!await ReplaceAsync(result, replacement, index)) break;
                    }
                    if (scenario.Replacements.Length > 0) result.ReplacementPassed = result.Replacements.All(step => step.Applied);
                    EvaluationChecks.Content(scenario, result);
                    result.EndToEndReady = result.Checks.Values.All(value => value) && result.ReplacementPassed != false &&
                        TaskDocumentValidator.ValidateRelease(input, result.Document).Count == 0;
                    await SaveAsync();
                    if (report.JudgeEnabled)
                    {
                        stage = "review";
                        var review = await AttemptAsync(step => result.Judge = step, (evidence, token) => HebrewJudge.ReviewAsync(capture,
                            scenario.Prompt + "\n" + string.Join('\n', scenario.Refinements),
                            HebrewJudge.CollectTexts(plan, result.Document), options.MaxOutputTokens, token, evidence));
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

        async Task<bool> AuthorPlanAsync(EvaluationCase scenario, EvaluationResult result)
        {
            var context = new List<AuthoringTurn>();
            var messages = new[] { scenario.Prompt }.Concat(scenario.Refinements).ToArray();
            for (var index = 0; index < messages.Length; index++)
            {
                stage = index == 0 ? "authoring" : "refinement";
                var message = messages[index];
                var reply = await AttemptAsync(step =>
                {
                    if (index == 0) result.Authoring = step;
                    else result.Refinements[index - 1] = step;
                }, (evidence, token) => engine.AuthorAsync(new TemplateAuthoringInput(message, result.Plan, context.ToArray()), token, evidence));
                if (reply is null) { result.InterpretationPassed = false; await SaveAsync(); return false; }
                var call = index == 0 ? result.Authoring! : result.Refinements[index - 1];
                if (reply.Proposal is { } proposal)
                {
                    result.Plan = proposal;
                    call.Applied = true;
                    context.Clear();
                }
                else
                {
                    call.Outcome = "clarification";
                    context.Add(new("parent", message));
                    context.Add(new("assistant", reply.Clarification!));
                }
                await SaveAsync();
            }
            if (context.Count == 0 && result.Plan is not null) return true;
            result.InterpretationPassed = false;
            await SaveAsync();
            return false;
        }

        async Task<bool> ReplaceAsync(EvaluationResult result, EvaluationReplacement replacement, int index)
        {
            var input = result.Input!;
            var document = result.Document!;
            if (replacement.Stage == "replace-material")
            {
                var target = document.Materials.ElementAtOrDefault(replacement.TargetIndex);
                if (target is null) { result.Replacements[index].SkipReason = "missing-target"; return false; }
                var request = new MaterialReplacementInput(input, document, target.Id, replacement.Instruction);
                var candidate = await AttemptAsync(step => result.Replacements[index] = step,
                    (evidence, token) => engine.ReplaceMaterialAsync(request, token, evidence), input,
                    document.Materials, target.Id);
                if (candidate is null) return false;
                result.Document = TaskAssembly.ReplaceMaterial(request, candidate, result.Replacements[index].Metadata);
            }
            else
            {
                var target = document.Questions.ElementAtOrDefault(replacement.TargetIndex);
                if (target is null) { result.Replacements[index].SkipReason = "missing-target"; return false; }
                var request = new QuestionReplacementInput(input, document, target.Id, replacement.Instruction);
                var candidate = await AttemptAsync(step => result.Replacements[index] = step,
                    (evidence, token) => engine.ReplaceQuestionAsync(request, token, evidence), input, document.Materials, target.Id);
                if (candidate is null) return false;
                result.Document = TaskAssembly.ReplaceQuestion(request, candidate, result.Replacements[index].Metadata);
            }
            result.Replacements[index].Applied = true;
            await SaveAsync();
            return true;
        }

        async Task<T?> AttemptAsync<T>(Action<EvaluationStep> setStep, Func<AiCallEvidence, CancellationToken, Task<AiResult<T>>> operation,
            ResolvedTaskRequest? input = null, MaterialContent[]? sources = null, string? targetId = null) where T : class
        {
            EvaluationStep NewStep() => new()
            {
                Role = stage,
                InputFingerprint = input is null ? null : TaskRequestResolver.Fingerprint(input),
                Sources = sources?.Select(material => new MaterialRevision(material.Id, material.Revision)).ToArray() ?? [],
                TargetId = targetId
            };
            var step = NewStep();
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
                    step = NewStep();
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

        async Task<T?> AttemptOnceAsync<T>(EvaluationStep step, Func<AiCallEvidence, CancellationToken, Task<AiResult<T>>> operation) where T : class
        {
            step.StartedAtUtc = DateTime.UtcNow;
            capture.Current = step;
            PublishProgress();
            var started = Stopwatch.GetTimestamp();
            var evidence = new AiCallEvidence();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(options.RequestTimeout);
            try
            {
                var generated = await operation(evidence, deadline.Token);
                step.Metadata = generated.Metadata;
                step.ContractValid = true;
                step.Outcome = "accepted";
                return generated.Value;
            }
            catch (AiGenerationException exception)
            {
                step.StatusCode = exception.StatusCode;
                step.Failure = exception.ProblemType ?? exception.Message;
                step.ValidationErrors = exception.ValidationErrors;
                step.Outcome = "failed";
                if (exception.StatusCode == 429) throw;
                return null;
            }
            catch (TaskValidationException exception) { Reject(step, exception.Errors); return null; }
            catch (EvaluationCallLimitException) { step.Failure = "call-limit"; step.Outcome = "not-started"; throw; }
            catch (OperationCanceledException)
            {
                step.Failure = ct.IsCancellationRequested ? "cancelled" : "timeout";
                step.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                if (ct.IsCancellationRequested) throw;
                step.StatusCode = 504;
                return null;
            }
            catch (JsonException) { step.Failure = "invalid-review-json"; step.Outcome = "failed"; return null; }
            catch (Exception exception) when (exception is HttpRequestException or ClientResultException)
            {
                step.StatusCode = exception is ClientResultException response ? response.Status :
                    (int?)((HttpRequestException)exception).StatusCode;
                step.Failure = "provider-error";
                step.Outcome = "failed";
                if (step.StatusCode == 429) throw new AiGenerationException(429, "rate-limited");
                return null;
            }
            finally
            {
                step.Metadata ??= evidence.Metadata;
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

    private static EvaluationStep Skipped(string role, string reason) => new() { Role = role, Outcome = "skipped", SkipReason = reason };

    private static void Reject(EvaluationStep step, IReadOnlyDictionary<string, string[]> errors)
    {
        step.Outcome = "failed";
        step.ContractValid = false;
        step.Failure = "validation";
        step.ValidationErrors = errors;
    }
}
