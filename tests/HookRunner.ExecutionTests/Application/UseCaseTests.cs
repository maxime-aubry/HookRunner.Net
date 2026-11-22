using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>
/// Scénarios applicatifs de préparation et sélection observés à la frontière d'exécution isolée.
/// </summary>
public sealed class UseCaseTests
{
    /// <summary>Vérifie qu'un contexte sans stratégie préparée rend 2 sans appeler l'exécuteur.</summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>Vérifie que les noms inconnus, vides ou dupliqués empêchent toute exécution isolée.</summary>
    /// <param name="selected">Nom de hook demandé.</param>
    /// <param name="first">Nom de la première stratégie enregistrée.</param>
    /// <param name="second">Nom de la seconde stratégie enregistrée.</param>
    /// <param name="diagnostic">Fragment attendu dans l'erreur de sélection.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>Vérifie qu'un timeout ou un chemin d'état invalide est refusé avant l'exécution isolée.</summary>
    /// <param name="timeout">Timeout global à valider.</param>
    /// <param name="state">Chemin d'état configuré.</param>
    /// <param name="diagnostic">Fragment attendu dans l'erreur de préparation.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>Vérifie qu'un chemin d'état enraciné est refusé avant le lancement du hook.</summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task RootedStateFilePathPreventsExecution()
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, Path.Combine(fixture.Root, "Hooks", "state.json")),
            new Context(executor, diagnostics));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", null, []), fixture.Invocation),
                TestContext.Current.CancellationToken));

        Assert.Contains("StateFilePath", exception.Message, StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    /// <summary>Vérifie qu'un chemin d'état sortant de la racine est refusé avant le lancement du hook.</summary>
    /// <param name="statePath">Chemin relatif configuré qui remonte hors de la solution.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("..")]
    [InlineData("../state.json")]
    [InlineData("../sibling/state.json")]
    public async Task StateFilePathOutsideSolutionPreventsExecution(string statePath)
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, statePath), new Context(executor, diagnostics));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", null, []), fixture.Invocation),
                TestContext.Current.CancellationToken));

        Assert.Contains("StateFilePath", exception.Message, StringComparison.Ordinal);
        Assert.Null(executor.Request);
    }

    /// <summary>
    /// Vérifie que la première solution ascendante gagne.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task TheNearestSolutionWins()
    {
        using SolutionFixture fixture = new();
        string nearest = Path.Combine(fixture.Root, "agent");
        File.WriteAllText(Path.Combine(nearest, "Nested.sln"), "");
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, "Hooks/state.json"), new Context(executor, diagnostics));

        await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", null, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(nearest, executor.Request!.Options.SolutionRoot);
    }

    [Fact]
    public async Task CommandTimeoutOverridesConfiguration()
    {
        using SolutionFixture fixture = new();
        RecordingExecutor executor = new();
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new TestHook("chosen")],
            new HookOptionsPreparer(120, "Hooks/state.json"), new Context(executor, diagnostics));

        await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("chosen", 17, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(17, executor.Request!.Options.TimeoutSeconds);
    }

    /// <summary>Vérifie que l'absence de solution ascendante empêche tout appel de l'exécuteur.</summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>
    /// Vérifie la sélection insensible à la casse d'un seul hook et les options préparées depuis le dossier de
    /// l'agent.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>Stratégie nommée de test qui refuse toute exécution directe dans le superviseur.</summary>
    /// <param name="name">Nom exposé par la stratégie sentinelle de test.</param>
    private sealed class TestHook(string name) : HookStrategy
    {
        /// <summary>Nom du hook ou du projet cible représenté par ce type.</summary>
        public override string Name => name;

        /// <summary>Détecte une exécution directe accidentelle du hook par le superviseur.</summary>
        /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
        /// <param name="options">Options préparées pour cette invocation du hook.</param>
        /// <param name="cancellationToken">
        /// Token d'annulation transmis aux opérations qui le prennent en charge.
        /// </param>
        /// <returns>Ne retourne jamais de tâche : l'appel lève immédiatement une exception.</returns>
        /// <exception cref="InvalidOperationException">
        /// Le cas d'usage doit déléguer à l'exécuteur isolé plutôt qu'appeler cette stratégie.
        /// </exception>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not execute the strategy in process.");
    }

    /// <summary>Double d'exécution conservant la requête pour les assertions du cas d'usage.</summary>
    private sealed class RecordingExecutor : IIsolatedHookExecutor
    {
        /// <summary>
        /// Dernière requête mémorisée par le double d'exécution, ou null avant le premier appel.
        /// </summary>
        public IsolatedHookRequest? Request { get; private set; }

        /// <summary>Mémorise la dernière requête reçue et simule un succès sans lancer d'enfant.</summary>
        /// <param name="request">
        /// Requête isolée contenant le nom du hook, l'invocation et les options préparées.
        /// </param>
        /// <param name="cancellationToken">
        /// Token d'annulation transmis aux opérations qui le prennent en charge.
        /// </param>
        /// <returns>Une tâche déjà terminée contenant un succès sans diagnostic.</returns>
        /// <remarks>Le token n'est pas observé car ce double n'effectue aucune attente.</remarks>
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        }
    }
}
