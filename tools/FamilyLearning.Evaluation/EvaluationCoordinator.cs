using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

public sealed record EvaluationActiveRun(string Id, bool Running, string Status, EvaluationProgress? Progress, string? Error);

/// <summary>Owns one run and its cancellation until completion or host shutdown. Pollers see immutable snapshots.</summary>
public sealed class EvaluationCoordinator : IHostedService, IDisposable
{
    private readonly object gate = new();
    private readonly EvaluationRunStore store;
    private readonly ServiceProvider? aiProvider;
    private readonly IChatClient? client;
    private readonly AiGenerationOptions? options;
    private readonly Dictionary<string, string?> profile = [];
    private CancellationTokenSource? cancellation;
    private Task run = Task.CompletedTask;
    private EvaluationActiveRun? active;
    private bool stopping;

    /// <summary>Isolates shared AI registration and validation so configuration failures cannot disable offline work.</summary>
    /// <remarks><paramref name="configureAi"/> supplies optional test overrides after application AI registration.</remarks>
    public EvaluationCoordinator(IConfiguration configuration, IHostEnvironment environment, EvaluationRunStore store,
        Action<IServiceCollection>? configureAi = null)
    {
        this.store = store;
        ServiceProvider? provider = null;
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddTaskAi(configuration, environment);
            configureAi?.Invoke(services);
            provider = services.BuildServiceProvider();
            // Resolve the same validators explicitly, without attaching ValidateOnStart to the dashboard host.
            var validatedOptions = provider.GetRequiredService<IOptions<AiGenerationOptions>>().Value;
            var configuredClient = provider.GetServices<IChatClient>().SingleOrDefault();
            if (configuredClient is null) return;
            profile = EvaluationPlan.CaptureProfile(configuration, validatedOptions);
            options = validatedOptions;
            client = configuredClient;
            aiProvider = provider;
            provider = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OptionsValidationException or ArgumentException or FormatException)
        {
            // Do not retain configuration errors or raw values; they can contain credentials.
        }
        finally { provider?.Dispose(); }
    }

    public bool Configured => client is not null;
    public Dictionary<string, string?> Profile => new(profile);
    public EvaluationActiveRun? Active { get { lock (gate) return active; } }

    public async Task<string> StartRunAsync(EvaluationRunRequest request)
    {
        if (!request.Confirmed) throw new ArgumentException("Confirm the billable run before starting.");
        if (request.Prototype) throw new ArgumentException("The temporary prototype requires the CLI and a pre-registered cost budget.");
        var plan = await EvaluationPlan.LoadAsync(request);
        plan.ValidateBudget();
        if (client is null) throw new AiGenerationException(503,
            "AI configuration is missing or invalid. Fix it and restart the dashboard before starting a real evaluation.");
        lock (gate)
        {
            if (stopping || active?.Running == true) throw new InvalidOperationException("A run is already active or the host is stopping.");
            var id = EvaluationRunStore.NewId();
            var directory = store.DirectoryFor(id);
            Directory.CreateDirectory(directory);
            var report = plan.CreateReport(Profile);
            cancellation?.Dispose();
            cancellation = new();
            active = new(id, true, "running", null, null);
            // The coordinator owns this task; it never inherits a browser request's cancellation.
            run = ExecuteAsync(id, report, directory, cancellation.Token);
            return id;
        }
    }

    public void Cancel(string id)
    {
        lock (gate)
        {
            if (active?.Id != id || !active.Running) throw new InvalidOperationException("This run is not active.");
            cancellation!.Cancel();
        }
    }

    public Task<EvaluationSummary> SaveReviewAsync(string id, EvaluationReviewUpdate update)
    {
        lock (gate)
        {
            if (active?.Id == id && active.Running) throw new InvalidOperationException("Wait for the run to finish before reviewing.");
        }
        return store.SaveReviewAsync(id, update);
    }

    public Task WaitAsync() { lock (gate) return run; }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (gate) { stopping = true; cancellation?.Cancel(); pending = run; }
        // Await the final checkpoint, even when the host's graceful-shutdown deadline expires.
        await pending;
    }

    private async Task ExecuteAsync(string id, EvaluationReport report, string directory, CancellationToken ct)
    {
        string? error = null;
        try
        {
            await EvaluationRunner.RunAsync(client!, options!, report, directory, ct, progress =>
            {
                lock (gate) active = new(id, true, report.Status, progress, null);
            });
        }
        catch (Exception) { report.Status = "failed"; error = "Evaluation failed. Check AI configuration and artifact permissions."; }
        finally
        {
            lock (gate) active = new(id, false, report.Status, active?.Progress, error);
        }
    }

    public void Dispose()
    {
        cancellation?.Dispose();
        aiProvider?.Dispose();
    }
}
