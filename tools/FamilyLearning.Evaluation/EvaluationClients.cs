using System.ClientModel.Primitives;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Evaluation;

/// <summary>The app's AI adapter as configured, plus the pinned judge on the same key, endpoint and limits.</summary>
/// <remarks>Each client is owned and disposed by its own service provider.</remarks>
public sealed class EvaluationClients : IDisposable
{
    // Only access and limits are shared; the judge never inherits the generator's model, routing, reasoning or sampling.
    private static readonly string[] SharedKeys = ["Ai:ApiKey", "OPENROUTER_API_KEY", "Ai:Endpoint", "Ai:MaxOutputTokens",
        "Ai:RequestTimeoutSeconds", "Ai:MaxRequestBytes", "Ai:MaxSchemaBytes"];
    private readonly ServiceProvider generatorServices;
    private readonly ServiceProvider judgeServices;

    private EvaluationClients(ServiceProvider generatorServices, ServiceProvider judgeServices, IChatClient client,
        IChatClient judge, AiGenerationOptions options, Dictionary<string, string?> profile, Dictionary<string, string?> judgeProfile)
    {
        this.generatorServices = generatorServices;
        this.judgeServices = judgeServices;
        Client = client;
        Judge = judge;
        Options = options;
        Profile = profile;
        JudgeProfile = judgeProfile;
    }

    public IChatClient Client { get; }
    public IChatClient Judge { get; }
    public AiGenerationOptions Options { get; }
    public Dictionary<string, string?> Profile { get; }
    public Dictionary<string, string?> JudgeProfile { get; }

    /// <summary>Null when no API key is configured. Invalid configuration throws; callers must not retain its message.</summary>
    /// <remarks><paramref name="configure"/> applies test overrides to both clients' services after app registration.</remarks>
    public static EvaluationClients? Create(IConfiguration configuration, IHostEnvironment environment,
        Action<IServiceCollection>? configure = null, PipelineTransport? transport = null)
    {
        var judgeValues = SharedKeys.Where(key => configuration[key] is not null).ToDictionary(key => key, key => configuration[key]);
        judgeValues["Ai:Model"] = HebrewJudge.Model;
        judgeValues["Ai:ResponseFormat"] = "json_schema";
        judgeValues["Ai:ReasoningEffort"] = HebrewJudge.ReasoningEffort;
        var judgeConfiguration = new ConfigurationBuilder().AddInMemoryCollection(judgeValues).Build();
        ServiceProvider? generator = null, judge = null;
        try
        {
            generator = Build(configuration);
            judge = Build(judgeConfiguration);
            var options = generator.GetRequiredService<IOptions<AiGenerationOptions>>().Value;
            var judgeOptions = judge.GetRequiredService<IOptions<AiGenerationOptions>>().Value;
            if (generator.GetServices<IChatClient>().SingleOrDefault() is not { } client ||
                judge.GetServices<IChatClient>().SingleOrDefault() is not { } judgeClient) return null;
            var clients = new EvaluationClients(generator, judge, client, judgeClient, options,
                AiProfile.Capture(configuration, options), AiProfile.Capture(judgeConfiguration, judgeOptions));
            generator = judge = null;
            return clients;
        }
        finally
        {
            generator?.Dispose();
            judge?.Dispose();
        }

        ServiceProvider Build(IConfiguration source)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddTaskAi(source, environment, transport);
            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }
    }

    public void Dispose()
    {
        generatorServices.Dispose();
        judgeServices.Dispose();
    }
}
