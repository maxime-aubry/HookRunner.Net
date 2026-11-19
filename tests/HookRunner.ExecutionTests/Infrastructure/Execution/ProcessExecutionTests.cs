using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios d'intégration du cas d'usage avec des processus enfants réels et contrôlés.</summary>
public sealed class ProcessExecutionTests
{
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
            new Context(executor, diagnostics));

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
