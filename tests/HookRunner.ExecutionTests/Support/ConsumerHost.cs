using System.Diagnostics;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Exécute une copie isolée du consommateur et capture ses flux avec un nettoyage garanti.</summary>
internal sealed class ConsumerHost(string directory, bool useAppHost = false)
{
    private readonly ChildProcessCommand command = ConsumerProgram.CopyTo(Path.Combine(directory, "compiled-host"), useAppHost);

    public async Task<HostResult> RunAsync(string[] arguments, string json, HostSimulation? simulation = null)
    {
        ProcessStartInfo start = CreateStartInfo(arguments, simulation);
        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        return await RunStartedAsync(process, json, timeout.Token);
    }

    private ProcessStartInfo CreateStartInfo(string[] arguments, HostSimulation? simulation)
    {
        ProcessStartInfo start = new(command.FileName)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        if (simulation is not null)
        {
            start.Environment["HOOKRUNNER_TEST_MODE"] = simulation.Mode;
            if (simulation.Result is not null)
            {
                start.Environment["HOOKRUNNER_TEST_RESULT"] = simulation.Result;
            }
        }

        return start;
    }

    private static async Task<HostResult> RunStartedAsync(Process process, string json, CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureAsync(process, json, cancellationToken);
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

    private static async Task<HostResult> CaptureAsync(Process process, string json, CancellationToken cancellationToken)
    {
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(json.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        return new HostResult(process.Id, process.ExitCode, await output, await error);
    }
}

internal sealed record HostSimulation(string Mode, string? Result);

internal sealed record HostResult(int ProcessId, int ExitCode, string Output, string Error);
