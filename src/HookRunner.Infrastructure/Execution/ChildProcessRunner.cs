using System.Diagnostics;

namespace HookRunner.Infrastructure;

/// <summary>Possède le processus enfant, draine ses flux et confirme son arrêt avant de terminer.</summary>
internal sealed class ChildProcessRunner(ChildProcessCommand command)
{
    public async Task<int> RunAsync(string workingDirectory, string requestPath, string resultPath, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = CreateStartInfo(workingDirectory, requestPath, resultPath);
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Process.Start(start)!;
        await RunStartedProcessAsync(process, cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory, string requestPath, string resultPath)
    {
        ProcessStartInfo start = new(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(ChildHookProtocol.Command);
        start.ArgumentList.Add(requestPath);
        start.ArgumentList.Add(resultPath);
        return start;
    }

    private static async Task RunStartedProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await DrainAndWaitAsync(process, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await EnsureTerminatedAsync(process).ConfigureAwait(false);
        }
    }

    private static async Task DrainAndWaitAsync(Process process, CancellationToken cancellationToken)
    {
        process.StandardInput.Close();
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
    }

    private static async Task EnsureTerminatedAsync(Process process)
    {
        if (!process.HasExited)
        {
            using CancellationTokenSource termination = new(TimeSpan.FromSeconds(5));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(termination.Token).ConfigureAwait(false);
        }
    }
}
