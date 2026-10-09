using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Preview-first command for the fixed activity probes, separate from the normal evaluation report format.</summary>
internal static class ActivityProbeCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var live = false;
        decimal? budget = null;
        var output = "artifacts/evaluations";
        var name = "contract";
        var model = ActivityProbeModel.Sol;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var flag = args[index];
            if (!seen.Add(flag)) throw new ArgumentException("Duplicate probe flag.");
            if (flag == "--live") { live = true; continue; }
            if (++index >= args.Length) throw new ArgumentException("Missing probe value.");
            if (flag == "--budget-usd" && decimal.TryParse(args[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount is > 0 and <= 6)
                budget = amount;
            else if (flag == "--output" && !string.IsNullOrWhiteSpace(args[index])) output = args[index];
            else if (flag == "--protocol" && args[index] is "contract" or "edits") name = args[index];
            else if (flag == "--model" && args[index] is "sol" or "gemini") model = ActivityProbeModel.Select(args[index]);
            else throw new ArgumentException("Invalid probe flag or value.");
        }
        if (live && budget is null) throw new ArgumentException("Live probe requires an explicit budget.");
        var protocol = ActivityContractProbe.Select(name);
        var maxCalls = protocol.Sum(item => item.Stages.Length);
        if (maxCalls > ActivityProbeTransport.MaxCalls) throw new InvalidOperationException("Probe protocol exceeds the transport call limit.");
        Console.WriteLine($"Activity {name} probe: {protocol.Length} synthetic cases, at most {maxCalls} calls, one repetition; no retries or judge.");
        foreach (var item in protocol) Console.WriteLine($"  {item.Id}: {string.Join(", ", item.Stages)} ({item.Stages.Length})\n    {item.Message}");
        Console.WriteLine($"Strict {model.Model} / medium / 16,384 output tokens; only {string.Join(", ", model.Providers)}, no provider fallback, " +
            $"${model.PromptPerMillion.ToString(CultureInfo.InvariantCulture)}/M input and ${model.CompletionPerMillion.ToString(CultureInfo.InvariantCulture)}/M output routing caps.");
        Console.WriteLine("64 KiB request limit; reserve before each call, retaining unknown costs. Maximum calculated reserve: $" +
            (maxCalls * model.Reserve(ActivityProbeTransport.MaxRequestBytes)).ToString(CultureInfo.InvariantCulture) + "; lower budgets may stop early.");
        if (!live) { Console.WriteLine("Preview only. Add --live --budget-usd AMOUNT (0 < AMOUNT <= 6) after explicit authorization."); return 0; }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        // Only the model changes between profiles; every other production setting stays as configured.
        builder.Configuration["Ai:Model"] = model.Model;
        var suffix = model == ActivityProbeModel.Sol ? "" : model.Name + "-";
        var directory = Path.GetFullPath(Path.Combine(output, $"activity-{name}-{suffix}" + EvaluationRunStore.NewId()));
        using var handler = new ActivityProbeTransport(budget!.Value, directory, new HttpClientHandler { AllowAutoRedirect = false }, model);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var clients = EvaluationClients.Create(builder.Configuration, builder.Environment, transport: new HttpClientPipelineTransport(http)) ??
            throw new ArgumentException("Configure the app's provider key before an authorized live probe.");
        using var service = new AiGenerationService([clients.Client], NullLogger<AiGenerationService>.Instance, Options.Create(clients.Options));
        var report = new ActivityProbeReport { Profile = clients.Profile };
        Directory.CreateDirectory(directory);
        var protocolSaved = false;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await ActivityContractProbe.RunAsync(name, service, report, Save, cancellation.Token);
            report.Status = "completed";
        }
        catch (OperationCanceledException) { report.Status = "cancelled"; }
        catch (Exception error)
        {
            report.Status = "failed";
            report.Failure = handler.StopReason ?? (error as ActivityProbeCheckException)?.Check ?? "stage-or-validation-failure";
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            report.FinishedAtUtc = DateTime.UtcNow;
            await Save();
        }
        Console.WriteLine($"{report.Status}{(report.Failure is null ? "" : " (" + report.Failure + ")")}: {handler.Calls.Count}/{maxCalls} calls; accounted cost including unknown reserves ${handler.AccountedUsd.ToString(CultureInfo.InvariantCulture)}. Evidence: {directory}");
        return report.Status == "completed" ? 0 : report.Status == "cancelled" ? 130 : 1;

        async Task Save()
        {
            if (!protocolSaved && report.Fixtures is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "protocol.json"), JsonSerializer.Serialize(new
                {
                    cases = protocol,
                    fixtures = report.Fixtures,
                    protocol = name,
                    maxCalls,
                    budgetUsd = budget,
                    maxOutputTokens = 16384,
                    maxRequestBytes = 65536,
                    model = model.Model,
                    routing = new { only = model.Providers, allow_fallbacks = false, require_parameters = true, max_price = new { prompt = model.PromptPerMillion, completion = model.CompletionPerMillion } },
                    inputTokenReserve = "wire UTF-8 byte count + 4096",
                    pricingSource = $"https://openrouter.ai/api/v1/models/{model.Model}/endpoints"
                }, EvaluationFiles.Json));
                protocolSaved = true;
            }
            var path = Path.Combine(directory, "probe.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(report, EvaluationFiles.Json));
            File.Move(path + ".tmp", path, true);
        }
    }
}
