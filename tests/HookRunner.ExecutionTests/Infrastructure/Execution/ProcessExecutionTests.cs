using System.Diagnostics;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios d'intégration du cas d'usage avec des processus enfants réels et contrôlés.</summary>
[Collection("Processes")]
public sealed class ProcessExecutionTests
{
    /// <summary>Le budget de démarrage est conservé et l'échéance arrête un hook C# ignorant toute annulation.</summary>
    /// <returns>Une tâche vérifiant le code 0 et la terminaison forcée de l'enfant à l'échéance.</returns>
    [Fact]
    public async Task GlobalTimeoutForciblyTerminatesANonCooperatingChild()
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        ChildProcessCommand command = ConsumerProgram.Command;
        command = command with { Arguments = [.. command.Arguments, "--test-mode", "uncooperative-wait", ""] };
        using StringWriter diagnostics = new();
        using CancellationTokenSource watchdog = new(TimeSpan.FromSeconds(30));
        Context context = new(new ProcessHookExecutor(command), diagnostics, new FileHookStateStore(), clock);
        context.SetStrategy(new ConsumerStrategy("inspect"), WithOutput(fixture),
            new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json")));
        string output = Path.Combine(fixture.Root, "observation.json");

        await using RunningInvocation<int> running = new(context.ExecuteAsync, TestContext.Current.CancellationToken);
        Task<int> execution = running.Execution;
        clock.Advance(TimeSpan.FromSeconds(119));
        int childId = await ConsumerProcessObservation.WaitForProcessIdAsync(output, execution, watchdog.Token);
        Assert.False(execution.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        int exit = await execution.WaitAsync(TimeSpan.FromSeconds(10), watchdog.Token);

        Assert.Equal(0, exit);
        Assert.Contains("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("succeeded", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId));
    }

    /// <summary>
    /// Vérifie via le cas d'usage les codes et diagnostics d'un véritable enfant pour chaque statut ou panne de
    /// protocole.
    /// </summary>
    /// <param name="hook">Nom du hook enregistré et demandé.</param>
    /// <param name="mode">Mode simulé par le consommateur de test.</param>
    /// <param name="result">Contenu brut à renvoyer en mode invalid-result.</param>
    /// <param name="expectedCode">Code de sortie applicatif attendu.</param>
    /// <param name="diagnostic">Fragment de diagnostic attendu.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("inspect", "", "", 0, "succeeded")]
    [InlineData("inspect", "technical-failure", "", 2, "Consumer technical failure")]
    [InlineData("inspect", "unexpected-exit", "", 2, "terminated unexpectedly")]
    [InlineData("inspect", "invalid-result", "null", 2, "Invalid hook child result")]
    [InlineData("inspect", "invalid-result", "{}", 2, "Hook execution failed")]
    [InlineData("inspect", "invalid-result", "{\"Version\":1}", 2, "Hook execution failed")]
    [InlineData("inspect", "invalid-result", "{\"Version\":2,\"Status\":0}", 2, "Invalid hook child result")]
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
            new Context(new ProcessHookExecutor(command), diagnostics, new FileHookStateStore()));
        InvocationData invocation = WithOutput(fixture);

        RunHookResponse response = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs(hook, null, []), invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, response.ExitCode);
        Assert.Contains(diagnostic, diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Vérifie qu'une erreur de lancement réelle est transformée en code 2 avec un diagnostic technique.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task FailureToStartTheChildIsATechnicalError()
    {
        using SolutionFixture fixture = new();
        using StringWriter diagnostics = new();
        ProcessHookExecutor executor = new(new ChildProcessCommand(Path.Combine(fixture.Root, "missing-executable"), []));
        RunHookUseCase useCase = new([new ConsumerStrategy("inspect")], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(executor, diagnostics, new HookRunner.Infrastructure.FileHookStateStore()));

        RunHookResponse response = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("inspect", null, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, response.ExitCode);
        Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Vérifie qu'une annulation extérieure arrête un enfant déjà prêt puis rend le code 2.</summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    /// <remarks>
    /// Le test attend la publication du PID pour synchroniser l'annulation et vérifie ensuite que ce processus
    /// n'existe plus. Des échéances techniques bornent le scénario.
    /// </remarks>
    [Fact]
    public async Task ExternalCancellationTerminatesTheChildAndReportsTwo()
    {
        using SolutionFixture fixture = new();
        ChildProcessCommand command = ConsumerProgram.Command;
        command = command with { Arguments = [.. command.Arguments, "--test-mode", "wait", ""] };
        using StringWriter diagnostics = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        RunHookUseCase useCase = new([new ConsumerStrategy("inspect")], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(new ProcessHookExecutor(command), diagnostics, new FileHookStateStore()));
        string output = Path.Combine(fixture.Root, "observation.json");

        RunHookRequest request = new(new RunHookArgs("inspect", null, []), WithOutput(fixture));
        await using RunningInvocation<RunHookResponse> running = new(token => useCase.ExecuteAsync(request, token), TestContext.Current.CancellationToken);
        Task<RunHookResponse> execution = running.Execution;
        int childId = await ConsumerProcessObservation.WaitForProcessIdAsync(output, execution, deadline.Token);
        running.Cancel();
        RunHookResponse response = await execution.WaitAsync(deadline.Token);

        Assert.Equal(2, response.ExitCode);
        Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId));
    }

    /// <summary>
    /// Ajoute à l'invocation de la fixture le chemin d'observation attendu par le hook consommateur.
    /// </summary>
    /// <param name="fixture">Solution temporaire dans laquelle écrire l'observation.</param>
    /// <returns>Une copie de l'invocation contenant observation_path dans OriginalJson.</returns>
    private static InvocationData WithOutput(SolutionFixture fixture) => fixture.Invocation with
    {
        OriginalJson = JsonSerializer.Serialize(new { observation_path = Path.Combine(fixture.Root, "observation.json") }),
    };

    /// <summary>Stratégie sentinelle qui refuse toute exécution directe dans le superviseur.</summary>
    /// <param name="name">Nom exposé par la stratégie sentinelle de test.</param>
    private sealed class ConsumerStrategy(string name) : HookStrategy
    {
        /// <summary>Nom du hook ou du projet cible représenté par ce type.</summary>
        public override string Name => name;
        /// <summary>
        /// Détecte tout appel direct de la stratégie dans le superviseur au lieu de l'exécuteur isolé.
        /// </summary>
        /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
        /// <param name="options">Options préparées pour cette invocation du hook.</param>
        /// <param name="cancellationToken">
        /// Token d'annulation transmis aux opérations qui le prennent en charge.
        /// </param>
        /// <returns>Ne retourne jamais de tâche : l'appel lève immédiatement une exception.</returns>
        /// <exception cref="InvalidOperationException">
        /// Cette stratégie sentinelle doit toujours être exécutée via le processus enfant.
        /// </exception>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not run this hook in process.");
    }
}
