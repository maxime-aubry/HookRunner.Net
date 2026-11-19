using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleTestsGroup;

[Collection("Console")]
public sealed class CliTests
{
    [Fact]
    public async Task HostDisposesItsComposedServicesAfterExecution()
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":{\"StateFilePath\":\"Hooks/state.json\"}}");
        AsyncConsumerService? dependency = null;

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"], services =>
        {
            services.AddSingleton<AsyncConsumerService>();
            services.AddSingleton<HookStrategy>(provider =>
            {
                dependency = provider.GetRequiredService<AsyncConsumerService>();
                return new ConsumerHook();
            });
            services.AddSingleton<IIsolatedHookExecutor>(new CapturingExecutor());
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(dependency!.Disposed);
    }

    [Fact]
    public async Task ProjectSelectionIsRejectedBeforeAnyHookExecutesInThisMilestone()
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":{\"StateFilePath\":\"Hooks/state.json\"}}");
        CapturingExecutor executor = new();

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex", "--projects", "Example"], services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Project selection", console.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    public static IEnumerable<TheoryDataRow<string[]>> InvalidArguments()
    {
        yield return new([]);
        yield return new(["execute"]);
        yield return new(["wrong", "hook"]);
        yield return new(["execute", " "]);
        yield return new(["execute", "--agent", "codex"]);
        yield return new(["execute", "hook"]);
        yield return new(["execute", "hook", "--agent"]);
        yield return new(["execute", "hook", "--agent", "claude"]);
        yield return new(["execute", "hook", "--agent", "codex", "--agent", "codex"]);
        yield return new(["execute", "hook", "--agent", "codex", "--unknown", "value"]);
        foreach (string timeout in new[] { "0", "-1", "1.5", "text", "2147483648" })
        {
            yield return new(["execute", "hook", "--agent", "codex", "--timeout", timeout]);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidCommandsReturnTwoWithDiagnostics(string[] arguments)
    {
        using ConsoleCapture console = new("{}");

        int exit = await HookRunnerHost.RunAsync(arguments, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("HookRunner:", console.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigurationIsLoadedFromTheCompiledProgramAndTheResponseIsPropagated()
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":{\"StateFilePath\":\"Hooks/state.json\"}}");
        CapturingExecutor executor = new();

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex", "--timeout", "19"], services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(19, executor.Request!.Options.TimeoutSeconds);
        Assert.Equal(fixture.Root, executor.Request.Options.SolutionRoot);
    }

    [Theory]
    [InlineData("{}", "StateFilePath")]
    [InlineData("{\"HookRunner\":{\"TimeoutSeconds\":\"bad\",\"StateFilePath\":\"state.json\"}}", "TimeoutSeconds")]
    [InlineData("invalid JSON", "Failed to load configuration")]
    public async Task InvalidConfigurationPreventsExecution(string settings, string diagnostic)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new(settings);
        CapturingExecutor executor = new();

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"], services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains(diagnostic, console.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildRunsPreparedInvocationWithoutReadingNativeInputOrConfiguration(bool fails)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new("not an event");
        using ConfigurationFile configuration = new("not configuration");
        TargetProject project = new("Prepared", Path.Combine(fixture.Root, "Prepared.csproj"), fixture.Root);
        HookOptions options = new(29, fixture.Root, fixture.Root, [project], "prepared-state");
        IsolatedHookRequest request = new("HOOK", fixture.Invocation, options);
        string input = Path.Combine(fixture.Root, "prepared.json");
        string output = Path.Combine(fixture.Root, "result.json");
        File.WriteAllText(input, JsonSerializer.Serialize(request));
        ConsumerHook hook = new() { Fails = fails };

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command, input, output], services =>
        {
            services.AddSingleton<HookStrategy>(hook);
            services.AddSingleton<IHookOptionsPreparer>(_ => throw new InvalidOperationException("Must not prepare in child."));
            services.AddSingleton<IIsolatedHookExecutor>(_ => throw new InvalidOperationException("Must not launch another child."));
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(fixture.Invocation, hook.Invocation);
        Assert.Equal(29, hook.Options!.TimeoutSeconds);
        Assert.Equal("prepared-state", hook.Options.StateFilePath);
        Assert.Equal("Prepared", Assert.Single(hook.Options.Projects).Name);
        ChildHookResult result = JsonSerializer.Deserialize<ChildHookResult>(File.ReadAllText(output))!;
        Assert.Equal(1, result.Version);
        Assert.Equal(fails ? HookExecutionStatus.TechnicalFailure : HookExecutionStatus.Success, result.Status);
        if (fails)
        {
            Assert.Contains("consumer failure", result.Diagnostic, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("invalid JSON")]
    public async Task ChildRejectsInvalidPreparedRequest(string json)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new("");
        string input = Path.Combine(fixture.Root, "request.json");
        string output = Path.Combine(fixture.Root, "result.json");
        File.WriteAllText(input, json);

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command, input, output], _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        ChildHookResult result = JsonSerializer.Deserialize<ChildHookResult>(File.ReadAllText(output))!;
        Assert.Equal(HookExecutionStatus.TechnicalFailure, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Fact]
    public async Task InvalidChildArgumentsReturnTwo()
    {
        using ConsoleCapture console = new("");

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command], _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Invalid hook child arguments", console.Error.ToString(), StringComparison.Ordinal);
    }

    private static string EventJson(string directory) => JsonSerializer.Serialize(new
    {
        session_id = "session-42",
        hook_event_name = "Stop",
        cwd = directory,
    });

    private sealed class ConsumerHook : HookStrategy
    {
        public override string Name => "hook";
        public bool Fails { get; init; }
        public InvocationData? Invocation { get; private set; }
        public HookOptions? Options { get; private set; }

        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
        {
            Invocation = invocation;
            Options = options;
            return Fails ? Task.FromException(new InvalidOperationException("consumer failure")) : Task.CompletedTask;
        }
    }

    private sealed class CapturingExecutor : IIsolatedHookExecutor
    {
        public IsolatedHookRequest? Request { get; private set; }

        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        }
    }

    private sealed class AsyncConsumerService : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposed = true;
        }
    }

    private sealed class ConfigurationFile : IDisposable
    {
        private readonly string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private readonly string? previous;

        public ConfigurationFile(string json)
        {
            previous = File.Exists(path) ? File.ReadAllText(path) : null;
            File.WriteAllText(path, json);
        }

        public void Dispose()
        {
            if (previous is null)
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, previous);
            }
        }
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextReader previousInput = Console.In;
        private readonly TextWriter previousError = Console.Error;
        private readonly StringReader input;
        public StringWriter Error { get; } = new();

        public ConsoleCapture(string json)
        {
            input = new StringReader(json);
            Console.SetIn(input);
            Console.SetError(Error);
        }

        public void Dispose()
        {
            Console.SetIn(previousInput);
            Console.SetError(previousError);
            input.Dispose();
            Error.Dispose();
        }
    }
}
