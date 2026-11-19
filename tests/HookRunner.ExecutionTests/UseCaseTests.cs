using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

public sealed class UseCaseTests
{
    [Fact]
    public async Task AnUnconfiguredContextReportsATechnicalErrorWithoutStartingAnything()
    {
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        Context context = new(executor, diagnostics);

        int exit = await context.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("No hook strategy", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    [Theory]
    [InlineData("missing", "first", "second", "Unknown hook")]
    [InlineData("first", "first", "FIRST", "Duplicate hook")]
    [InlineData("first", "first", " ", "non-empty Name")]
    public async Task InvalidHookConfigurationPreventsAnyExecution(string selected, string first, string second, string diagnostic)
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook(first), new TestHook(second)],
            new HookOptionsPreparer(120, "Hooks/state.json"), new Context(executor, diagnostics));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs(selected, null, []), fixture.Invocation),
                TestContext.Current.CancellationToken));

        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    [Theory]
    [InlineData(0, "Hooks/state.json", "TimeoutSeconds")]
    [InlineData(-1, "Hooks/state.json", "TimeoutSeconds")]
    [InlineData(120, "", "StateFilePath")]
    public async Task InvalidOptionsPreventExecution(int timeout, string state, string diagnostic)
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(timeout, state), new Context(executor, diagnostics));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", null, []), fixture.Invocation),
                TestContext.Current.CancellationToken));

        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task TheNearestSolutionWinsAndCommandTimeoutOverridesConfiguration()
    {
        using SolutionFixture fixture = new();
        string nearest = Path.Combine(fixture.Root, "agent");
        File.WriteAllText(Path.Combine(nearest, "Nested.sln"), "");
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, "Hooks/state.json"), new Context(executor, diagnostics));

        await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", 17, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(nearest, executor.Request!.Options.SolutionRoot);
        Assert.Equal(17, executor.Request.Options.TimeoutSeconds);
    }

    [Fact]
    public async Task MissingSolutionPreventsExecution()
    {
        using SolutionFixture fixture = new();
        File.Delete(Path.Combine(fixture.Root, "Example.slnx"));
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, "Hooks/state.json"), new Context(executor, diagnostics));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", null, []), fixture.Invocation),
                TestContext.Current.CancellationToken));

        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task ResolvesOneHookByNameAndPreparesOptionsFromTheAgentDirectory()
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        Context context = new(executor, diagnostics);
        RunHookUseCase useCase = new(
            [new TestHook("chosen"), new TestHook("other")],
            new HookOptionsPreparer(120, "Hooks/state.json"), context);
        RunHookRequest request = new(new RunHookArgs("CHOSEN", null, []), fixture.Invocation);

        RunHookResponse response = await useCase.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(0, response.ExitCode);
        IsolatedHookRequest execution = Assert.IsType<IsolatedHookRequest>(executor.Request);
        Assert.Equal("chosen", execution.HookName);
        Assert.Equal(fixture.Invocation, execution.Invocation);
        Assert.Equal(fixture.Root, execution.Options.SolutionRoot);
        Assert.Equal(fixture.Root, execution.Options.DefaultWorkingDirectory);
        Assert.Equal(120, execution.Options.TimeoutSeconds);
        Assert.Empty(execution.Options.Projects);
        Assert.Equal(Path.Combine(fixture.Root, "Hooks", "state.json"), execution.Options.StateFilePath);
        Assert.Contains("succeeded", diagnostics.ToString(), StringComparison.Ordinal);
    }

    private sealed class TestHook(string name) : HookStrategy
    {
        public override string Name => name;

        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not execute the strategy in process.");
    }

    private sealed class RecordingExecutor : IIsolatedHookExecutor
    {
        public IsolatedHookRequest? Request { get; private set; }

        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        }
    }
}
