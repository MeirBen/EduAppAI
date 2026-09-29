using System.Diagnostics;

namespace FamilyLearning.Api.Tests.Fixtures;

/// <summary>Drains both output pipes concurrently and kills stalled command trees after 30 seconds.</summary>
internal static class TestProcess
{
    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, string input = "")
    {
        start.UseShellExecute = false;
        start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
