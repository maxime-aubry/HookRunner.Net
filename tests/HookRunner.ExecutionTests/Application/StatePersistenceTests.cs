using HookRunner.Application;
using HookRunner.Domain;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Vérifie les verdicts publics et le compteur quand la sauvegarde est interrompue.</summary>
public sealed class StatePersistenceTests
{
    /// <summary>Une sauvegarde annulée ne publie ni le verdict métier ni la modification du compteur.</summary>
    /// <param name="status">Résultat du hook dont la sauvegarde doit être interrompue.</param>
    /// <param name="cancelExternally">Annule aussi l'invocation depuis l'appelant.</param>
    /// <param name="expire">Atteint l'échéance pendant la sauvegarde.</param>
    /// <returns>Une tâche vérifiant le diagnostic et le compteur à travers l'invocation suivante.</returns>
    [Theory]
    [InlineData(HookExecutionStatus.Success, false, true)]
    [InlineData(HookExecutionStatus.Success, true, false)]
    [InlineData(HookExecutionStatus.Success, true, true)]
    [InlineData(HookExecutionStatus.ValidationFailure, false, true)]
    [InlineData(HookExecutionStatus.ValidationFailure, true, false)]
    [InlineData(HookExecutionStatus.ValidationFailure, true, true)]
    public async Task InterruptedSavePreservesTheCounterAndExternalCancellationTakesPriority(
        HookExecutionStatus status, bool cancelExternally, bool expire)
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        FileHookStateStore store = new();
        using StringWriter diagnostics = new();
        HookExecutionStatus initialStatus = status == HookExecutionStatus.Success
            ? HookExecutionStatus.ValidationFailure : HookExecutionStatus.Success;
        int initialExit = await ExecuteAsync(fixture.Invocation, options, initialStatus, store, diagnostics,
            TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal(status == HookExecutionStatus.Success ? 2 : 0, initialExit);
        diagnostics.GetStringBuilder().Clear();

        ManualTimeProvider clock = new();
        using CancellationTokenSource external = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        SaveInterceptingStore interruptedStore = new(() =>
        {
            if (expire)
            {
                clock.Advance(TimeSpan.FromSeconds(120));
            }

            if (cancelExternally)
            {
                external.Cancel();
            }
        });

        int exit = await ExecuteAsync(fixture.Invocation, options, status, interruptedStore, diagnostics, clock, external.Token);

        Assert.Equal(cancelExternally ? 2 : 0, exit);
        Assert.Contains(cancelExternally ? "Hook execution failed" : "termination confirmed", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("succeeded", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("requested corrections", diagnostics.ToString(), StringComparison.Ordinal);
        if (cancelExternally)
        {
            Assert.DoesNotContain("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        }

        int nextExit = await ExecuteAsync(fixture.Invocation, options, HookExecutionStatus.ValidationFailure,
            store, diagnostics, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal(status == HookExecutionStatus.Success ? 0 : 2, nextExit);
    }

    /// <summary>Une panne de sauvegarde reste technique même après l'échéance, sans perdre le compteur ni son verrou.</summary>
    /// <param name="unrelatedCancellation">Lève une annulation indépendante plutôt qu'une erreur d'entrée-sortie.</param>
    /// <returns>Une tâche vérifiant le diagnostic technique et la prochaine demande de correction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFailureAfterTheDeadlineRemainsTechnicalAndReleasesTheIdentity(bool unrelatedCancellation)
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        FileHookStateStore store = new();
        using StringWriter diagnostics = new();
        Assert.Equal(2, await ExecuteAsync(fixture.Invocation, options, HookExecutionStatus.ValidationFailure,
            store, diagnostics, TimeProvider.System, TestContext.Current.CancellationToken));
        diagnostics.GetStringBuilder().Clear();

        ManualTimeProvider clock = new();
        using CancellationTokenSource independent = new();
        SaveInterceptingStore failingStore = new(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(120));
            throw unrelatedCancellation
                ? new OperationCanceledException("State save failed.", independent.Token)
                : new IOException("State save failed.");
        });

        int exit = await ExecuteAsync(fixture.Invocation, options, HookExecutionStatus.Success,
            failingStore, diagnostics, clock, TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Hook execution failed: State save failed.", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("succeeded", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await ExecuteAsync(fixture.Invocation, options, HookExecutionStatus.ValidationFailure,
            store, diagnostics, TimeProvider.System, TestContext.Current.CancellationToken));
    }

    /// <summary>Exécute une invocation préparée à travers le contexte public.</summary>
    /// <param name="invocation">Identité et données de l'invocation.</param>
    /// <param name="options">Budget et plafond préparés.</param>
    /// <param name="status">Résultat à retourner depuis l'exécution isolée.</param>
    /// <param name="store">Stockage réel ou interception de sa sauvegarde.</param>
    /// <param name="diagnostics">Destination des diagnostics publics.</param>
    /// <param name="clock">Horloge contrôlant le budget.</param>
    /// <param name="cancellationToken">Annulation extérieure.</param>
    /// <returns>Le code de sortie public de l'invocation.</returns>
    private static Task<int> ExecuteAsync(InvocationData invocation, HookOptions options, HookExecutionStatus status,
        IHookStateStore store, TextWriter diagnostics, TimeProvider clock, CancellationToken cancellationToken)
    {
        Context context = new(new ResultExecutor(status), diagnostics, store, clock);
        context.SetStrategy(new ConsumerHook(), invocation, options);
        return context.ExecuteAsync(cancellationToken);
    }

    /// <summary>Injecte une interruption juste avant la sauvegarde réelle sans modifier sa politique d'annulation.</summary>
    /// <param name="beforeSave">Action déclenchée avant chaque sauvegarde.</param>
    private sealed class SaveInterceptingStore(Action beforeSave) : IHookStateStore
    {
        /// <inheritdoc />
        public async Task<IHookStateSession> OpenAsync(string stateFilePath, HookIdentity identity, CancellationToken cancellationToken)
        {
            IHookStateSession session = await new FileHookStateStore().OpenAsync(stateFilePath, identity, cancellationToken);
            return new SaveInterceptingSession(session, beforeSave);
        }
    }

    /// <summary>Conserve la possession de la session réelle pendant l'interruption injectée.</summary>
    /// <param name="session">Session réelle possédant le verrou de l'identité.</param>
    /// <param name="beforeSave">Action exécutée avant de déléguer la sauvegarde.</param>
    private sealed class SaveInterceptingSession(IHookStateSession session, Action beforeSave) : IHookStateSession
    {
        /// <inheritdoc />
        public int Count => session.Count;

        /// <inheritdoc />
        public Task SaveAsync(int count, CancellationToken cancellationToken)
        {
            beforeSave();
            return session.SaveAsync(count, cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }

    /// <summary>Retourne le verdict isolé choisi sans lancer le hook dans le contexte.</summary>
    /// <param name="status">Statut à retourner.</param>
    private sealed class ResultExecutor(HookExecutionStatus status) : IIsolatedHookExecutor
    {
        /// <inheritdoc />
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new HookExecutionResult(status, "Fix the violation"));
    }
}
