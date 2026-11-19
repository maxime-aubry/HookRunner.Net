using System.Diagnostics;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

public sealed class HostTests
{
    [Theory]
    [InlineData("unexpected-exit", null, "terminated unexpectedly")]
    [InlineData("technical-failure", null, "Consumer technical failure")]
    [InlineData("duplicate", null, "Duplicate hook")]
    [InlineData("invalid-result", "null", "Invalid hook child result")]
    [InlineData("invalid-result", "{}", "Hook execution failed")]
    [InlineData("invalid-result", "{\"Version\":1,\"Status\":99}", "Invalid hook child result")]
    [InlineData("invalid-result", "{\"Version\":1,\"Status\":2}", "Invalid hook child result")]
    [InlineData("invalid-result", "garbage", "Hook execution failed")]
    public async Task ChildFailuresAreDiagnosedOnStderr(string mode, string? internalResult, string diagnostic)
    {
        using SolutionFixture fixture = new();
        string observation = Path.Combine(fixture.Root, "observation.json");
        string json = JsonSerializer.Serialize(new
        {
            session_id = "session-42",
            hook_event_name = "Stop",
            cwd = fixture.Root,
            observation_path = observation,
        });

        HostResult result = await RunHostAsync(["execute", "inspect", "--agent", "codex"], json, fixture.Root, mode, internalResult);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(diagnostic, result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
        Assert.False(File.Exists(observation));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("garbage")]
    public async Task PreparationFailuresDoNotStartTheConsumerHook(string json)
    {
        using SolutionFixture fixture = new();

        HostResult result = await RunHostAsync(["execute", "inspect", "--agent", "codex"], json, fixture.Root);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("HookRunner:", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerExecutesItsOwnHookInAChildWithTheSameComposition(bool useAppHost)
    {
        using SolutionFixture fixture = new();
        string observationPath = Path.Combine(fixture.Root, "observation.json");
        string json = JsonSerializer.Serialize(new
        {
            session_id = "session-42",
            hook_event_name = "Stop",
            cwd = fixture.Invocation.WorkingDirectory,
            observation_path = observationPath,
        });
        // The launch directory has a conflicting configuration: only the compiled host's file applies.
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), "not valid JSON");

        HostResult result = await RunHostAsync(["execute", "INSPECT", "--agent", "codex"], json, fixture.Root, useAppHost: useAppHost);

        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.Error}");
        Assert.Empty(result.Output);
        Assert.Contains("succeeded", result.Error, StringComparison.Ordinal);
        using JsonDocument observation = JsonDocument.Parse(File.ReadAllText(observationPath));
        JsonElement root = observation.RootElement;
        Assert.NotEqual(result.ProcessId, root.GetProperty("ProcessId").GetInt32());
        Assert.Equal("consumer-service", root.GetProperty("Dependency").GetString());
        InvocationData invocation = root.GetProperty("Invocation").Deserialize<InvocationData>()!;
        Assert.Equal(json, invocation.OriginalJson);
        Assert.Equal("session-42", invocation.SessionId);
        HookOptions options = root.GetProperty("Options").Deserialize<HookOptions>()!;
        Assert.Equal(37, options.TimeoutSeconds);
        Assert.Equal(fixture.Root, options.SolutionRoot);
        Assert.Equal(fixture.Root, options.DefaultWorkingDirectory);
        Assert.Empty(options.Projects);
        Assert.False(File.Exists(options.StateFilePath));
    }

    private static async Task<HostResult> RunHostAsync(string[] arguments, string json, string directory, string? mode = null, string? internalResult = null, bool useAppHost = false)
    {
        ChildProcessCommand command = ConsumerProgram.CopyTo(Path.Combine(directory, "compiled-host"), useAppHost);
        ProcessStartInfo start = new(command.FileName)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (mode is not null)
        {
            start.Environment["HOOKRUNNER_TEST_MODE"] = mode;
        }

        if (internalResult is not null)
        {
            start.Environment["HOOKRUNNER_TEST_RESULT"] = internalResult;
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(json.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return new HostResult(process.Id, process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private sealed record HostResult(int ProcessId, int ExitCode, string Output, string Error);
}
