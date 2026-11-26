using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Contrats de timeout observés à la frontière publique du contexte, avec temps contrôlé.</summary>
public sealed class TimeoutTests
{
    /// <summary>Le démarrage et les étapes du hook consomment le même budget sans grâce coopérative.</summary>
    /// <returns>Une tâche vérifiant la demande d'arrêt à l'échéance et l'attente de sa confirmation.</returns>
    [Fact]
    public async Task StartupAndExecutionShareOneBudgetAndRequestImmediateTermination()
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<HookExecutionResult> terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken executionToken = default;
        CallbackExecutor executor = new(async token =>
        {
            executionToken = token;
            clock.Advance(TimeSpan.FromSeconds(119));
            started.SetResult();
            return await terminated.Task;
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore(), clock);
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        Task<int> execution = context.ExecuteAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(executionToken.IsCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(1));
        bool terminationRequested = executionToken.IsCancellationRequested;
        bool waitedForConfirmation = !execution.IsCompleted;
        terminated.SetException(new OperationCanceledException(executionToken));
        int exit = await execution;

        Assert.True(terminationRequested);
        Assert.True(waitedForConfirmation);
        Assert.Equal(0, exit);
        Assert.Contains("termination confirmed", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Un échec de confirmation reste technique, même si son attente échoue par annulation.</summary>
    /// <param name="cancelledConfirmation">Simule une échéance technique distincte du token d'exécution.</param>
    /// <returns>Une tâche vérifiant le code 2 et le diagnostic technique après expiration.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTerminationConfirmationReportsTwo(bool cancelledConfirmation)
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        using CancellationTokenSource confirmation = new();
        CallbackExecutor executor = new(token =>
        {
            clock.Advance(TimeSpan.FromSeconds(120));
            Exception failure = cancelledConfirmation
                ? new OperationCanceledException("Termination was not confirmed.", confirmation.Token)
                : new TimeoutException("Termination was not confirmed.");
            return Task.FromException<HookExecutionResult>(failure);
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore(), clock);
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        int exit = await context.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Termination was not confirmed", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("timed out", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Une annulation extérieure prime sur un verdict tardif et sur le timeout simultané.</summary>
    /// <param name="expire">Déclenche aussi l'échéance globale.</param>
    /// <returns>Une tâche vérifiant que l'annulation extérieure produit un diagnostic technique et le code 2.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalCancellationRemainsTechnicalEvenAtTheDeadline(bool expire)
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        using CancellationTokenSource external = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CallbackExecutor executor = new(token =>
        {
            if (expire)
            {
                clock.Advance(TimeSpan.FromSeconds(120));
            }

            external.Cancel();
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore(), clock);
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        int exit = await context.ExecuteAsync(external.Token);

        Assert.Equal(2, exit);
        Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("succeeded", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("timed out", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Une annulation préalable empêche le lancement, même si l'adaptateur ignore le token.</summary>
    /// <returns>Une tâche vérifiant le code 2 sans lancement de l'exécuteur.</returns>
    [Fact]
    public async Task AlreadyCancelledInvocationDoesNotStartTheChild()
    {
        using SolutionFixture fixture = new();
        using StringWriter diagnostics = new();
        using CancellationTokenSource external = new();
        external.Cancel();
        bool started = false;
        CallbackExecutor executor = new(token =>
        {
            started = true;
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore());
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        int exit = await context.ExecuteAsync(external.Token);

        Assert.Equal(2, exit);
        Assert.False(started);
    }

    /// <summary>Le plus grand entier accepté reste un budget unique malgré plusieurs minuteries techniques.</summary>
    /// <returns>Une tâche vérifiant le timeout à l'échéance exacte avec une horloge contrôlée.</returns>
    [Fact]
    public async Task MaximumRepresentableTimeoutExpiresWithoutRenewingItsBudget()
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        CallbackExecutor executor = new(token =>
        {
            clock.Advance(TimeSpan.FromSeconds(int.MaxValue - 1));
            Assert.False(token.IsCancellationRequested);
            clock.Advance(TimeSpan.FromSeconds(1));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore(), clock);
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(int.MaxValue, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        int exit = await context.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("timed out", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Ignore le verdict métier arrivé après l'échéance globale et trace le timeout.</summary>
    /// <param name="dispatchTimers">False retarde les rappels pour vérifier le contrôle monotone de l'échéance.</param>
    /// <returns>Une tâche vérifiant que le résultat tardif est remplacé par un diagnostic de timeout.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateBusinessResultIsIgnoredAfterTheGlobalDeadline(bool dispatchTimers)
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        CallbackExecutor executor = new(token =>
        {
            clock.Advance(TimeSpan.FromSeconds(120), dispatchTimers);
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.ValidationFailure, "late verdict"));
        });
        Context context = new(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore(), clock);
        context.SetStrategy(new SentinelHook(), fixture.Invocation, new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));

        int exit = await context.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("late verdict", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Double de l'exécution isolée dont le résultat et les incidents sont contrôlés par le scénario.</summary>
    /// <param name="execute">Comportement externe à simuler.</param>
    private sealed class CallbackExecutor(Func<CancellationToken, Task<HookExecutionResult>> execute) : IIsolatedHookExecutor
    {
        /// <summary>Délègue l'exécution à la simulation du scénario.</summary>
        /// <param name="request">Requête déjà préparée.</param>
        /// <param name="cancellationToken">Token global de l'invocation.</param>
        /// <returns>La tâche de résultat ou d'échec fournie par le scénario.</returns>
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
            => execute(cancellationToken);
    }

    /// <summary>Stratégie qui ne doit jamais s'exécuter dans le superviseur.</summary>
    private sealed class SentinelHook : HookStrategy
    {
        public override string Name => "timeout-hook";

        /// <summary>Détecte une exécution sans isolation.</summary>
        /// <param name="invocation">Données préparées.</param>
        /// <param name="options">Options préparées.</param>
        /// <param name="cancellationToken">Token d'annulation.</param>
        /// <returns>Ne retourne jamais.</returns>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not execute the strategy.");
    }
}
