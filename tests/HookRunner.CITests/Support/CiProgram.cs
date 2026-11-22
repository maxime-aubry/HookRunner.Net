using System.Diagnostics;
using HookRunner.CI;

namespace HookRunner.CITests;

/// <summary>Lance le programme CI réel et garantit la fin du processus de test.</summary>
internal static class CiProgram
{
    public static async Task<(int ExitCode, string Error)> RunAsync(string[] arguments)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(CoverageGate).Assembly.Location);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await WaitForResultAsync(process, timeout.Token);
    }

    private static async Task<(int ExitCode, string Error)> WaitForResultAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
