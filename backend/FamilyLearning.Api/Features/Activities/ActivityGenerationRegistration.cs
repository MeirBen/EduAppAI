using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Registers the single-process content worker with existing persistence/provider services and validated polling options.</summary>
public static class ActivityGenerationRegistration
{
    public static void AddActivityGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AiGenerationService>();
        services.AddOptions<GenerationOperationOptions>().Bind(configuration.GetSection("GenerationOperations"))
            .Validate(o => o.PollIntervalSeconds is >= 1 and <= 60, "GenerationOperations:PollIntervalSeconds must be between 1 and 60.").ValidateOnStart();
        services.AddSingleton(p => new GenerationWorker(p.GetRequiredService<IServiceScopeFactory>(), p.GetRequiredService<AiGenerationService>(),
            p.GetRequiredService<TimeProvider>(), p.GetRequiredService<IOptions<GenerationOperationOptions>>(), p.GetRequiredService<IOptions<AiGenerationOptions>>(), configuration));
        services.AddHostedService(p => p.GetRequiredService<GenerationWorker>());
    }
}
