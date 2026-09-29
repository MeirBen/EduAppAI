using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>Opt-in developer command. Preview never resolves a provider or requires credentials.</summary>
public static class EvaluationCommand
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--compare")
            {
                if (args.Length != 3) throw new ArgumentException("Compare requires exactly two run.json paths.");
                var comparison = await EvaluationComparison.ReadAsync(args[1], args[2]);
                Console.WriteLine(JsonSerializer.Serialize(comparison, EvaluationFiles.Json));
                return comparison.DirectlyComparable ? 0 : 1;
            }
            var options = EvaluationOptions.Parse(args);
            var suite = await EvaluationFiles.LoadFixtureAsync<EvaluationCase>("cases.json");
            var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
            EvaluationFiles.ValidateCalibrationSamples(controls.Items);
            var cases = suite.Items;
            if (options.Case != "all") cases = cases.Where(scenario => scenario.Id == options.Case).ToArray();
            if (cases.Length == 0) throw new ArgumentException("Unknown case. Use --case all to list the suite.");
            var calls = options.PlannedCalls(cases.Length, controls.Items.Length);
            Console.WriteLine($"{cases.Length} cases × {options.Repeat} repeats; at most {calls} API calls (budget {options.MaxCalls}).");
            foreach (var scenario in cases) Console.WriteLine($"  {scenario.Id}: {scenario.ReviewFocus}");
            if (!options.Live)
            {
                Console.WriteLine("Preview only. Add --live to make billable calls using the app's Ai configuration.");
                return 0;
            }
            if (calls > options.MaxCalls) throw new ArgumentException("Planned calls exceed --max-calls.");

            // Compose only the shared AI adapter, never the web host, authentication or database.
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.Services.AddTaskAi(builder.Configuration, builder.Environment);
            using var host = builder.Build();
            var client = host.Services.GetService<IChatClient>() ??
                throw new ArgumentException("Configure the app's OpenRouter API key before using --live.");
            var aiOptions = host.Services.GetRequiredService<IOptions<AiGenerationOptions>>().Value;
            var report = new EvaluationReport(cases, options.Repeat, suite.Sha256,
                CaptureProfile(builder.Configuration, aiOptions))
            {
                JudgeEnabled = options.Judge,
                MaxCalls = options.MaxCalls,
                CalibrationSha256 = controls.Sha256,
                CalibrationSamples = controls.Items
            };
            var directory = Path.GetFullPath(Path.Combine(options.Output, $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(directory);
            Console.WriteLine($"Model: {report.Profile["Model"]}; report: {Path.Combine(directory, "run.json")}");
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try { await EvaluationRunner.RunAsync(client, aiOptions, report, directory, cancellation.Token); }
            finally { Console.CancelKeyPress -= cancel; }
            var summary = EvaluationSummary.Create(report);
            PrintSummary(summary);
            if (report.Status == "cancelled") return 130;
            return report.Status == "completed" && report.AutomaticPasses == summary.PlannedCaseRuns &&
                (!report.JudgeEnabled || report.JudgeCalibrationPassed == true &&
                    summary.GeneratedContentReviews.Succeeded == summary.PlannedCaseRuns && !report.HasHebrewFindings) ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            OptionsValidationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            // Configuration/IO exceptions can contain secret values or paths; don't print their bodies.
            Console.Error.WriteLine(args.Length > 0 && args[0] == "--compare"
                ? "Cannot compare reports. Check file paths, format version 2 and human scores (0, 1, 2 or null)."
                : "Evaluation could not start or save its report. Check arguments, AI configuration and output permissions.");
            Console.Error.WriteLine("Usage: evaluate-ai.sh [--live] [--case ID|all] [--repeat 1..5] [--max-calls 1..100] [--judge] [--output DIR]");
            Console.Error.WriteLine("Offline comparison: evaluate-ai.sh --compare BASELINE/run.json CANDIDATE/run.json (format version 2)");
            return 2;
        }
    }

    private static void PrintSummary(EvaluationSummary summary)
    {
        Console.WriteLine($"Run: {summary.Status}; authoring {summary.Authoring.Succeeded} passed/{summary.Authoring.Failed} failed; " +
            $"generation {summary.Generation.Succeeded} passed/{summary.Generation.Failed} failed.");
        Console.WriteLine($"Scenario checks: {summary.ScenarioAutomaticPasses}/{summary.PlannedCaseRuns} passed; " +
            $"failures by check: {JsonSerializer.Serialize(summary.AutomaticFailuresByCheck)}.");
        Console.WriteLine($"Judge calibration: {(summary.JudgeEnabled ? summary.JudgeCalibrationPassed?.ToString() ?? "unfinished" : "disabled")}; " +
            $"{summary.CalibrationFailureCount} failed controls. Content reviews: {summary.GeneratedContentReviews.Failed} failed; " +
            $"generated Hebrew findings: {summary.GeneratedHebrewIssueCount}. Human review remains separate.");
        Console.WriteLine($"Reported cost subtotal: {summary.CostCredits.KnownTotal?.ToString() ?? "unknown"} credits; " +
            $"{summary.CostCredits.MissingCalls}/{summary.AttemptedCalls} calls have unknown cost. See summary.json and run.json.");
    }

    private static Dictionary<string, string?> CaptureProfile(IConfiguration configuration, AiGenerationOptions options)
    {
        string[] keys = ["Model", "FallbackModel", "ResponseFormat", "ReasoningEnabled", "ReasoningEffort", "ReasoningMaxTokens", "Temperature", "TopP", "TopK"];
        var profile = keys.ToDictionary(key => key, key => configuration[$"Ai:{key}"]);
        profile["ResponseFormat"] ??= "json_object";
        profile["MaxOutputTokens"] = options.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
        profile["RequestTimeoutSeconds"] = options.RequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return profile;
    }
}

/// <summary>Explicit limits prevent accidentally running a large paid suite; no live calls by default.</summary>
public sealed record EvaluationOptions(bool Live, string Case, int Repeat, int MaxCalls, bool Judge, string Output)
{
    public static EvaluationOptions Parse(string[] args)
    {
        var options = new EvaluationOptions(false, "reading-grade3", 1, 4, false, "artifacts/evaluations");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (!seen.Add(flag)) throw new ArgumentException("Duplicate option.");
            if (flag == "--live") { options = options with { Live = true }; continue; }
            if (flag == "--judge") { options = options with { Judge = true }; continue; }
            if (++i >= args.Length) throw new ArgumentException("Missing option value.");
            var value = args[i];
            options = flag switch
            {
                "--case" => options with { Case = value },
                "--repeat" when int.TryParse(value, out var repeat) && repeat is >= 1 and <= 5 => options with { Repeat = repeat },
                "--max-calls" when int.TryParse(value, out var max) && max is >= 1 and <= 100 => options with { MaxCalls = max },
                "--output" when !string.IsNullOrWhiteSpace(value) => options with { Output = value },
                _ => throw new ArgumentException("Unknown option or invalid value.")
            };
        }
        return options;
    }

    public int PlannedCalls(int caseCount, int calibrationCount)
    {
        if (Judge && calibrationCount <= 0) throw new ArgumentException("Judge calibration controls are required.");
        return EvaluationReport.CountCalls(caseCount, Repeat, Judge ? calibrationCount : 0);
    }
}
