using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Features.Children;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Library;

namespace FamilyLearning.Api.Features;

internal static class FeatureRegistration
{
    internal static void AddLearningFeatures(this IServiceCollection services)
    {
        services.AddScoped<ActivityService>();
        services.AddScoped<GenerationOperationService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<ChildAssignmentService>();
        services.AddScoped<ChildSessionService>();
        services.AddScoped<ParentReviewService>();
        services.AddScoped<ChildService>();
        services.AddScoped<ChildAuthService>();
        services.AddScoped<SnapshotService>();
        services.AddScoped<LibraryService>();
    }
}
