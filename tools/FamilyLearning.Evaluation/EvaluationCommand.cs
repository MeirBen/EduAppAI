using System.Security.Cryptography;
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
            var options = EvaluationOptions.Parse(args);
            var suite = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "cases.json"));
            var cases = JsonSerializer.Deserialize<EvaluationCase[]>(suite, EvaluationRunner.Json)!;
            if (options.Case != "all") cases = cases.Where(scenario => scenario.Id == options.Case).ToArray();
            if (cases.Length == 0) throw new ArgumentException("Unknown case. Use --case all to list the suite.");
            var calls = options.PlannedCalls(cases.Length);
            Console.WriteLine($"{cases.Length} cases × {options.Repeat} repeats; at most {calls} API calls (budget {options.MaxCalls}).");
            foreach (var scenario in cases) Console.WriteLine($"  {scenario.Id}: {scenario.ReviewFocus}");
            if (!options.Live)
            {
                Console.WriteLine("Preview only. Add --live to make billable calls using the app's Ai configuration.");
                return 0;
            }
            options.ValidateCallBudget(cases.Length);

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
            var report = new EvaluationReport(cases, options.Repeat, Convert.ToHexString(SHA256.HashData(suite)),
                CaptureProfile(builder.Configuration, aiOptions))
            { JudgeEnabled = options.Judge, MaxCalls = options.MaxCalls };
            var directory = Path.GetFullPath(Path.Combine(options.Output, $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(directory);
            Console.WriteLine($"Model: {report.Profile["Model"]}; report: {Path.Combine(directory, "run.json")}");
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try { await EvaluationRunner.RunAsync(client, aiOptions, report, directory, cancellation.Token); }
            finally { Console.CancelKeyPress -= cancel; }
            Console.WriteLine($"{report.Status}: {report.AutomaticPasses}/{cases.Length * options.Repeat} automatic passes; Hebrew quality needs review.");
            Console.WriteLine($"Reported cost: {report.ReportedCostCredits} credits for {report.CallsWithReportedCost}/{report.AttemptedCalls} calls; missing costs are unknown.");
            return report.Status == "cancelled" ? 130 : report.Status == "completed" &&
                report.AutomaticPasses == cases.Length * options.Repeat && report.JudgeChecksPassed ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            OptionsValidationException or IOException or UnauthorizedAccessException or JsonException)
        {
            // Configuration/IO exceptions can contain secret values or paths; don't print their bodies.
            Console.Error.WriteLine("Evaluation could not start or save its report. Check arguments, AI configuration and output permissions.");
            Console.Error.WriteLine("Usage: evaluate-ai.sh [--live] [--case ID|all] [--repeat 1..5] [--max-calls 1..100] [--judge] [--output DIR]");
            return 2;
        }
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

    public int ValidateCallBudget(int caseCount)
    {
        var planned = PlannedCalls(caseCount);
        if (planned > MaxCalls) throw new ArgumentException("Planned calls exceed --max-calls.");
        return planned;
    }

    public int PlannedCalls(int caseCount) => caseCount * Repeat * (Judge ? 3 : 2) + (Judge ? 2 : 0);
}
