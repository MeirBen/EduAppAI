using System.Diagnostics;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Temporary matched one-shot/split experiment over fixed plans, without application persistence or automatic repairs.</summary>
internal static class ContentWorkflowPrototype
{
    internal static int CountCalls(EvaluationCase[] cases, int repeat) =>
        cases.Sum(item => item.InitialPlan!.Materials.Any(m => m.Source == "generated") ? 3 : 2) * repeat;

    internal static async Task RunAsync(IChatClient client, AiGenerationOptions options, EvaluationReport report,
        string directory, CancellationToken ct, Action<EvaluationProgress>? progress, TimeProvider? timeProvider)
    {
        report.Experiment?.Validate();
        var capture = new EvaluationCapture(client, report.MaxCalls, report);
        using var engine = new AiGenerationService([capture], NullLogger<AiGenerationService>.Instance, Options.Create(options));
        await EvaluationFiles.SaveAsync(report, directory);
        try
        {
            foreach (var scenario in report.Cases)
            {
                var plan = scenario.InitialPlan!;
                var resolution = TaskRequestResolver.Resolve(plan, scenario.InitialInput ?? new(plan.Defaults));
                if (resolution.Value is not { } request) throw new InvalidDataException("Invalid fixed-plan input.");
                for (var repetition = 1; repetition <= report.Repeat; repetition++)
                {
                    // Alternate execution order to avoid systematically favoring one variant with provider conditions.
                    var variants = repetition % 2 == 1 ? new[] { "one-shot", "split" } : ["split", "one-shot"];
                    foreach (var variant in variants)
                    {
                        ct.ThrowIfCancellationRequested();
                        var result = new PrototypeResult(scenario.Id, repetition, variant, request);
                        report.PrototypeResults.Add(result);
                        if (variant == "one-shot")
                        {
                            var generated = await CallAsync(result, "one-shot", (evidence, token) => engine.GenerateOneShotAsync(request, token, evidence));
                            if (generated is not null)
                            {
                                result.Document = generated.Value;
                                result.Stages[^1].Call!.Applied = true;
                            }
                        }
                        else
                        {
                            if (TaskAssembly.PrepareMaterials(request, result.Document) is { } input)
                            {
                                var generated = await CallAsync(result, "materials", (evidence, token) => engine.GenerateMaterialsAsync(input, token, evidence));
                                if (generated is null) continue;
                                result.Document = TaskAssembly.AcceptMaterials(request, result.Document, generated.Value, generated.Metadata).Document!;
                                result.Stages[^1].Call!.Applied = true;
                                // Keep accepted material even when the following call fails or the run is cancelled.
                                await EvaluationFiles.SaveAsync(report, directory);
                            }
                            else result.Stages.Add(new("materials") { Outcome = "skipped" });
                            var questions = TaskAssembly.PrepareQuestions(request, result.Document);
                            var generatedQuestions = await CallAsync(result, "questions", (evidence, token) => engine.GenerateQuestionsAsync(questions, token, evidence));
                            if (generatedQuestions is not null)
                            {
                                result.Document = TaskAssembly.AcceptQuestions(request, result.Document, generatedQuestions.Value, generatedQuestions.Metadata);
                                result.Stages[^1].Call!.Applied = true;
                            }
                        }
                        result.Measurements = TextLength.Measure(request, result.Document);
                        result.Passed = TaskDocumentValidator.ValidateRelease(request, result.Document).Count == 0;
                        await EvaluationFiles.SaveAsync(report, directory);
                    }
                }
            }
            report.Status = "completed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { report.Status = "cancelled"; }
        catch (EvaluationCallLimitException) { report.Status = "call-limit"; }
        catch (EvaluationCostLimitException) { report.Status = "cost-limit"; }
        catch { report.Status = "failed"; throw; }
        finally
        {
            report.FinishedAtUtc = DateTime.UtcNow;
            await EvaluationFiles.SaveAsync(report, directory);
            await PrototypeExperiment.WriteBlindReviewAsync(report, directory);
        }

        async Task<AiResult<T>?> CallAsync<T>(PrototypeResult result, string name, Func<AiCallEvidence, CancellationToken, Task<AiResult<T>>> operation) where T : class
        {
            if (report.AttemptedCalls > 0 && report.CallDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(report.CallDelaySeconds), timeProvider ?? TimeProvider.System, ct);
            ct.ThrowIfCancellationRequested();
            var stage = new PrototypeStage(name)
            {
                Call = new()
                {
                    Role = name,
                    InputFingerprint = result.InputFingerprint,
                    Sources = result.Document.Materials.Select(material => new MaterialRevision(material.Id, material.Revision)).ToArray()
                }
            };
            result.Stages.Add(stage);
            var call = stage.Call;
            capture.Current = call;
            progress?.Invoke(new(name, result.CaseId, result.Repetition, report.AttemptedCalls, report.PlannedCalls,
                report.Status, null, report.ReportedCostCredits, report.Steps.Count(s => s.RequestSent && !s.CostCredits.HasValue)));
            var started = Stopwatch.GetTimestamp();
            var evidence = new AiCallEvidence();
            try
            {
                var generated = await operation(evidence, ct);
                call.Metadata = generated.Metadata;
                call.ContractValid = true;
                stage.Outcome = "accepted";
                return generated;
            }
            catch (AiGenerationException error)
            {
                call.StatusCode = error.StatusCode;
                call.Failure = error.Category;
                call.ValidationErrors = error.ValidationErrors;
                stage.Outcome = "failed";
                return null;
            }
            catch (OperationCanceledException)
            {
                call.Failure = "cancelled";
                stage.Outcome = "cancelled";
                throw;
            }
            catch (Exception error) when (error is EvaluationCallLimitException or EvaluationCostLimitException)
            {
                call.Failure = error is EvaluationCostLimitException ? "cost-limit" : "call-limit";
                stage.Outcome = "not-started";
                throw;
            }
            finally
            {
                call.Metadata ??= evidence.Metadata;
                call.Outcome = stage.Outcome;
                call.ElapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                call.FinishedAtUtc = DateTime.UtcNow;
                await EvaluationFiles.SaveAsync(report, directory);
            }
        }
    }
}
