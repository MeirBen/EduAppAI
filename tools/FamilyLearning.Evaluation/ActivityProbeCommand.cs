using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Preview-first command for the fixed activity contract probe, separate from the normal evaluation report format.</summary>
internal static class ActivityProbeCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var live = false;
        decimal? budget = null;
        var output = "artifacts/evaluations";
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
            else throw new ArgumentException("Invalid probe flag or value.");
        }
        if (live && budget is null) throw new ArgumentException("Live probe requires an explicit budget.");
        var protocol = ActivityContractProbe.Protocol;
        Console.WriteLine("Activity contract probe: 8 synthetic cases, at most 17 calls, one repetition; no retries or judge.");
        foreach (var item in protocol) Console.WriteLine($"  {item.Id}: {string.Join(", ", item.Stages)} ({item.Stages.Length})\n    {item.Message}");
        Console.WriteLine("Strict Sol / medium / 16,384 output tokens; standard OpenAI only, no provider fallback, $2/M input and $10/M output routing caps.");
        Console.WriteLine("64 KiB request limit; reserve before each call, retaining unknown costs. Maximum calculated reserve: $5.152768; lower budgets may stop early.");
        if (!live) { Console.WriteLine("Preview only. Add --live --budget-usd AMOUNT (0 < AMOUNT <= 6) after explicit authorization."); return 0; }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        var directory = Path.GetFullPath(Path.Combine(output, "activity-contract-" + EvaluationRunStore.NewId()));
        using var handler = new ActivityProbeTransport(budget!.Value, directory, new HttpClientHandler { AllowAutoRedirect = false });
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
            await ActivityContractProbe.RunAsync(service, report, Save, cancellation.Token);
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
        Console.WriteLine($"{report.Status}{(report.Failure is null ? "" : " (" + report.Failure + ")")}: {handler.Calls.Count}/17 calls; accounted cost including unknown reserves ${handler.AccountedUsd.ToString(CultureInfo.InvariantCulture)}. Evidence: {directory}");
        return report.Status == "completed" ? 0 : report.Status == "cancelled" ? 130 : 1;

        async Task Save()
        {
            if (!protocolSaved && report.Fixtures is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "protocol.json"), JsonSerializer.Serialize(new
                {
                    cases = protocol,
                    fixtures = report.Fixtures,
                    maxCalls = 17,
                    budgetUsd = budget,
                    maxOutputTokens = 16384,
                    maxRequestBytes = 65536,
                    routing = new { only = new[] { "openai" }, allow_fallbacks = false, require_parameters = true, max_price = new { prompt = 2, completion = 10 } },
                    inputTokenReserve = "wire UTF-8 byte count + 4096",
                    pricingSource = "https://openrouter.ai/api/v1/models/openai/gpt-6.1-sol/endpoints"
                }, EvaluationFiles.Json));
                protocolSaved = true;
            }
            var path = Path.Combine(directory, "probe.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(report, EvaluationFiles.Json));
            File.Move(path + ".tmp", path, true);
        }
    }
}
