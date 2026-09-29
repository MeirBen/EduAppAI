using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Exercises the app's prompts, schemas and validators without HTTP endpoints, identity or persistence.</summary>
public static class EvaluationRunner
{
    /// <summary>Runs sequentially without retries, checkpointing each stage. Cancellation retains partial results.</summary>
    public static async Task RunAsync(IChatClient client, AiGenerationOptions options, EvaluationReport report,
        string directory, CancellationToken ct)
    {
        var capture = new EvaluationCapture(client, report.MaxCalls);
        using var engine = new AiGenerationService([capture], NullLogger<AiGenerationService>.Instance, Options.Create(options));
        await SaveAsync();
        try
        {
            if (report.JudgeEnabled)
            {
                foreach (var sample in report.CalibrationSamples)
                {
                    ct.ThrowIfCancellationRequested();
                    Console.WriteLine($"Judge calibration: {sample.Id}");
                    var calibration = new CalibrationResult(sample, new());
                    report.Calibration.Add(calibration);
                    var review = await AttemptAsync(calibration.Call, token => HebrewJudge.ReviewAsync(
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
                    Console.WriteLine($"{scenario.Id} [{repetition}/{report.Repeat}]");
                    var result = new EvaluationResult(scenario.Id, repetition);
                    report.Results.Add(result);
                    result.Authoring = new();
                    var definition = await AttemptAsync(result.Authoring, token => engine.AuthorAsync(scenario.Prompt, token));
                    await SaveAsync();
                    if (definition is null) continue;

                    var supplied = new Dictionary<string, JsonElement>();
                    if (scenario.UseMaximumQuestionCount)
                    {
                        var count = definition.InstanceParameters.FirstOrDefault(parameter => parameter.Key == definition.Generation.QuestionCountParameter);
                        result.Checks["adjustableQuestionCount"] = count?.Max is not null;
                        if (count?.Max is { } maximum) supplied[count.Key] = JsonSerializer.SerializeToElement(maximum);
                    }
                    var parameters = ParameterValidator.Validate(definition.InstanceParameters, supplied);
                    result.Checks["parameterDefaults"] = parameters.Errors.Count == 0;
                    if (parameters.Errors.Count > 0) continue;
                    result.Parameters = parameters.Values;
                    result.Generation = new();
                    var content = await AttemptAsync(result.Generation, token => engine.GenerateAsync(definition, parameters.Values, token));
                    if (content is not null) CheckContent(scenario, content, result.Checks);
                    await SaveAsync();
                    if (content is not null && report.JudgeEnabled)
                    {
                        result.Judge = new();
                        var review = await AttemptAsync(result.Judge, token => HebrewJudge.ReviewAsync(capture,
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

        async Task<T?> AttemptAsync<T>(EvaluationStep step, Func<CancellationToken, Task<AiResult<T>>> operation) where T : class
        {
            capture.Current = step;
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

        Task SaveAsync() => EvaluationFiles.SaveAsync(report, directory);
    }

    private static void CheckContent(EvaluationCase scenario, TaskContent content, Dictionary<string, bool> checks)
    {
        checks["questionCount"] = content.Questions.Length == scenario.QuestionCount;
        checks["interaction"] = content.Questions.All(question => question.Interaction.Type == scenario.Interaction);
        if (scenario.ChoiceCount is { } count)
            checks["choiceCount"] = content.Questions.All(question => question.Interaction.Options?.Length == count);
        // Whitespace-delimited words are a reproducible length signal, not a Hebrew linguistic tokenizer.
        if (scenario.MinPassageWords.HasValue || scenario.MaxPassageWords.HasValue)
        {
            var words = content.ContentBlocks.Sum(block => block.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
            checks["passageLength"] = (!scenario.MinPassageWords.HasValue || words >= scenario.MinPassageWords) &&
                (!scenario.MaxPassageWords.HasValue || words <= scenario.MaxPassageWords);
        }
    }
}
