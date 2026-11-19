using System.Diagnostics;
using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

public sealed class ProcessHookExecutor(ChildProcessCommand command) : IIsolatedHookExecutor
{
    public async Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
    {
        string directory = Directory.CreateTempSubdirectory("hookrunner-").FullName;
        try
        {
            string inputPath = Path.Combine(directory, "request.json");
            string resultPath = Path.Combine(directory, "result.json");
            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
            ProcessStartInfo start = new(command.FileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = request.Options.DefaultWorkingDirectory,
            };
            foreach (string argument in command.Arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.ArgumentList.Add(ChildHookProtocol.Command);
            start.ArgumentList.Add(inputPath);
            start.ArgumentList.Add(resultPath);
            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using CancellationTokenSource termination = new(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(termination.Token).ConfigureAwait(false);
                }
            }

            if (process.ExitCode != 0 || !File.Exists(resultPath))
            {
                throw new InvalidDataException($"Hook child terminated unexpectedly (exit {process.ExitCode}).");
            }

            string json = await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false);
            ChildHookResult? result = JsonSerializer.Deserialize<ChildHookResult>(json);
            if (result is null || result.Version != ChildHookProtocol.Version
                || !Enum.IsDefined(result.Status)
                || (result.Status != HookExecutionStatus.Success && string.IsNullOrWhiteSpace(result.Diagnostic)))
            {
                throw new InvalidDataException("Invalid hook child result.");
            }

            return new HookExecutionResult(result.Status, result.Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
