using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

public sealed class ProcessExecutionTests
{
    [Theory]
    [InlineData("inspect", "", "", 0, "succeeded")]
    [InlineData("inspect", "technical-failure", "", 2, "Consumer technical failure")]
    [InlineData("inspect", "unexpected-exit", "", 2, "terminated unexpectedly")]
    [InlineData("inspect", "invalid-result", "null", 2, "Invalid hook child result")]
    [InlineData("inspect", "invalid-result", "{}", 2, "Hook execution failed")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1}", 2, "Hook execution failed")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1,\"Status\":99}", 2, "Invalid hook child result")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1,\"Status\":2}", 2, "Invalid hook child result")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1,\"Status\":2,\"Diagnostic\":\"technical detail\"}", 2, "technical detail")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1,\"Status\":1,\"Diagnostic\":\"please correct\"}", 2, "please correct")]
    [InlineData("inspect", "invalid-result", "garbage", 2, "Hook execution failed")]
    public async Task UseCaseSupervisesTheActualChild(string hook, string mode, string result, int expectedCode, string diagnostic)
    {
        using SolutionFixture fixture = new();
        ChildProcessCommand command = ConsumerProgram.Command;
        command = command with { Arguments = [.. command.Arguments, "--test-mode", mode, result] };
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new ConsumerStrategy(hook)], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(new ProcessHookExecutor(command), diagnostics));
        InvocationData invocation = WithOutput(fixture);

        RunHookResponse response = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs(hook, null, []), invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, response.ExitCode);
        Assert.Contains(diagnostic, diagnostics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureToStartTheChildIsATechnicalError()
    {
        using SolutionFixture fixture = new();
        using StringWriter diagnostics = new();
        ProcessHookExecutor executor = new(new ChildProcessCommand(Path.Combine(fixture.Root, "missing-executable"), []));
        RunHookUseCase useCase = new([new ConsumerStrategy("inspect")], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(executor, diagnostics));

        RunHookResponse response = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("inspect", null, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, response.ExitCode);
        Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalCancellationTerminatesTheChildAndReportsTwo()
    {
        using SolutionFixture fixture = new();
        ChildProcessCommand command = ConsumerProgram.Command;
        command = command with { Arguments = [.. command.Arguments, "--test-mode", "wait", ""] };
        using StringWriter diagnostics = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        RunHookUseCase useCase = new([new ConsumerStrategy("inspect")], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(new ProcessHookExecutor(command), diagnostics));
        string output = Path.Combine(fixture.Root, "observation.json");

        Task<RunHookResponse> execution = useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("inspect", null, []), WithOutput(fixture)), cancellation.Token);
        try
        {
            while (!File.Exists(output) && !execution.IsCompleted)
            {
                await Task.Delay(20, deadline.Token);
            }

            Assert.True(File.Exists(output), diagnostics.ToString());
            int childId = int.Parse(await File.ReadAllTextAsync(output, deadline.Token), CultureInfo.InvariantCulture);
            cancellation.Cancel();
            RunHookResponse response = await execution.WaitAsync(deadline.Token);

            Assert.Equal(2, response.ExitCode);
            Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId));
        }
        finally
        {
            cancellation.Cancel();
            await execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    private static InvocationData WithOutput(SolutionFixture fixture) => fixture.Invocation with
    {
        OriginalJson = JsonSerializer.Serialize(new { observation_path = Path.Combine(fixture.Root, "observation.json") }),
    };

    private sealed class ConsumerStrategy(string name) : HookStrategy
    {
        public override string Name => name;
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not run this hook in process.");
    }
}
