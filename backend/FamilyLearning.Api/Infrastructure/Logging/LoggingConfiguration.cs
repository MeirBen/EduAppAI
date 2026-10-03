using Serilog;

namespace FamilyLearning.Api.Infrastructure.Logging;

/// <summary>Host-owned sinks and enrichment configured after all application configuration providers have loaded.</summary>
public static class LoggingConfiguration
{
    /// <summary>Registers Serilog through the host's logging abstractions; the host owns and flushes its sinks.</summary>
    public static void AddApplicationLogging(this WebApplicationBuilder builder) =>
        builder.Services.AddSerilog((services, logging) => ConfigureLogger(logging, builder.Configuration, builder.Environment)
            .ReadFrom.Services(services), preserveStaticLogger: true);

    private static LoggerConfiguration ConfigureLogger(LoggerConfiguration logging, ConfigurationManager configuration, IHostEnvironment environment)
    {
        const string pathKey = "Serilog:WriteTo:File:Args:configure:0:Args:path";
        if (configuration[pathKey] is { Length: > 0 } path)
        {
            // dotnet run uses the server folder; published hosts without a solution use their content root.
            var root = environment.ContentRootPath;
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "FamilyLearning.sln")))
                {
                    root = directory.FullName;
                    break;
                }
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [pathKey] = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), root)
            });
        }

        // Sink failures and buffer overflow must remain visible even when the file sink cannot write.
        Serilog.Debugging.SelfLog.Enable(TextWriter.Synchronized(Console.Error));
        return logging.ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("Environment", environment.EnvironmentName);
    }
}
