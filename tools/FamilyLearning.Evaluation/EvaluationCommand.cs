using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
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
            if (args.Length > 0 && args[0] == "--ui")
            {
                await EvaluationDashboard.RunAsync(args);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--compare")
            {
                if (args.Length != 3) throw new ArgumentException("Compare requires exactly two run.json paths.");
                var comparison = await EvaluationComparison.ReadAsync(args[1], args[2]);
                Console.WriteLine(JsonSerializer.Serialize(comparison, EvaluationFiles.Json));
                return comparison.DirectlyComparable ? 0 : 1;
            }
            var options = EvaluationOptions.Parse(args);
            var plan = await EvaluationPlan.LoadAsync(new([options.Case], options.Repeat, options.Judge, options.MaxCalls,
                options.Label, options.RunNotes, CallDelaySeconds: options.CallDelaySeconds));
            Console.WriteLine($"{plan.Cases.Length} cases × {options.Repeat} repeats; {plan.PlannedCalls} base API calls (total budget: {options.MaxCalls}).");
            Console.WriteLine($"Pause between calls: {options.CallDelaySeconds} seconds; " + "up to three retries per stage for HTTP 429 within the total budget.");
            foreach (var scenario in plan.Cases) Console.WriteLine($"  {scenario.Id}: {scenario.ReviewFocus}");
            if (!options.Live)
            {
                Console.WriteLine("Preview only. Add --live to make billable calls using the app's Ai configuration.");
                return 0;
            }
            plan.ValidateBudget();
            // Compose only the shared AI adapter, never the web host, authentication or database.
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            using var clients = EvaluationClients.Create(builder.Configuration, builder.Environment) ??
                throw new ArgumentException("Configure the app's OpenRouter API key before using --live.");
            var report = plan.CreateReport(clients.Profile, clients.JudgeProfile);
            var directory = Path.GetFullPath(Path.Combine(options.Output, EvaluationRunStore.NewId()));
            Directory.CreateDirectory(directory);
            Console.WriteLine($"Model: {report.Profile["Model"]}{(report.JudgeEnabled ? $"; judge: {HebrewJudge.Model}" : "")}; report: {Path.Combine(directory, "run.json")}");
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                await EvaluationRunner.RunAsync(clients.Client, clients.Judge, clients.Options, report, directory, cancellation.Token,
                    progress => Console.WriteLine($"{progress.Stage}: {progress.CaseId} [{progress.Repetition}] — {progress.CompletedCalls}/{progress.PlannedCalls} calls"));
            }
            finally { Console.CancelKeyPress -= cancel; }
            var summary = EvaluationSummary.Create(report);
            PrintSummary(summary);
            if (report.Status == "cancelled") return 130;
            return report.Status == "completed" && report.AutomaticPasses == summary.PlannedCaseRuns &&
                (!report.JudgeEnabled || report.JudgeCalibrationPassed == true &&
                    summary.GeneratedContentReviews.Succeeded == summary.PlannedCaseRuns && !report.HasHebrewFindings) ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            OptionsValidationException or FormatException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            // Configuration/IO exceptions can contain secret values or paths; don't print their bodies.
            Console.Error.WriteLine(args.Length > 0 && args[0] == "--compare"
                ? $"Cannot compare reports. Check file paths, format version {EvaluationVersions.ReportFormat} and human scores (0, 1, 2 or null)."
                : "Evaluation could not start or save its report. Check arguments, AI configuration and output permissions.");
            Console.Error.WriteLine("Usage: evaluate-ai.sh [--live] [--case ID|all] [--repeat 1..5] [--max-calls 1..100] [--call-delay-seconds 0..60] [--judge] [--output DIR]");
            Console.Error.WriteLine("Dashboard: evaluate-ai.sh --ui [--port PORT] [--output DIR]");
            Console.Error.WriteLine($"Offline comparison: evaluate-ai.sh --compare BASELINE/run.json CANDIDATE/run.json (format version {EvaluationVersions.ReportFormat})");
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
}

/// <summary>Explicit limits prevent accidentally running a large paid suite; no live calls by default.</summary>
public sealed record EvaluationOptions(bool Live, string Case, int Repeat, int MaxCalls, bool Judge, string Output,
    string? Label = null, string? RunNotes = null, int CallDelaySeconds = 5)
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
                "--label" => options with { Label = value },
                "--notes" => options with { RunNotes = value },
                "--case" => options with { Case = value },
                "--repeat" when int.TryParse(value, out var repeat) && repeat is >= 1 and <= 5 => options with { Repeat = repeat },
                "--max-calls" when int.TryParse(value, out var max) && max is >= 1 and <= 100 => options with { MaxCalls = max },
                "--call-delay-seconds" when int.TryParse(value, out var delay) && delay is >= 0 and <= 60 => options with { CallDelaySeconds = delay },
                "--output" when !string.IsNullOrWhiteSpace(value) => options with { Output = value },
                _ => throw new ArgumentException("Unknown option or invalid value.")
            };
        }
        return options;
    }

}
