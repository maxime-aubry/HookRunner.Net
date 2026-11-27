using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Garanties de sérialisation observées entre superviseurs et hooks dans des processus distincts.</summary>
[Collection("Processes")]
public sealed class HostConcurrencyTests
{
    /// <summary>Une identité occupée expire sans enfant ; une autre identité termine avant sa libération.</summary>
    /// <param name="outcome">Verdict autorisé pour le premier hook : succès, demande métier ou erreur technique.</param>
    /// <param name="ownerExit">Code public attendu après libération du premier hook.</param>
    /// <param name="expectedLater">Demandes ultérieures révélant le compteur conservé ou remis à zéro.</param>
    /// <returns>Une tâche vérifiant le parallélisme, le timeout en attente et le compteur ultérieur.</returns>
    [Theory]
    [InlineData(0, 0, new[] { 2, 2, 2, 0 })]
    [InlineData(1, 2, new[] { 2, 0, 0, 0 })]
    [InlineData(2, 2, new[] { 2, 2, 0, 0 })]
    public async Task WaitingIdentityTimesOutWithoutStartingWhileAnotherIdentityCanFinish(
        int outcome, int ownerExit, int[] expectedLater)
    {
        using SolutionFixture fixture = new();
        ConsumerHost host = new(fixture.Root, instrument: false);
        Assert.Equal(2, (await host.RunAsync(Arguments(), Json(fixture, "shared"), new HostSimulation("validation-failure", null))).ExitCode);
        await using HookGate owner = new();
        await using RunningConsumer owning = new(host, Arguments(), Json(fixture, "shared"), new HostSimulation("gated", owner.Name));
        await owner.WaitForEntryAsync();

        await using HookGate waiting = new();
        string waitingObservation = Path.Combine(fixture.Root, waiting.Name + ".json");
        await using RunningConsumer timedOut = new(host, Arguments(1), Json(fixture, "shared", waitingObservation), new HostSimulation("gated", waiting.Name));
        await using HookGate independent = new();
        await using RunningConsumer parallel = new(host, Arguments(), Json(fixture, "independent"), new HostSimulation("gated", independent.Name));
        await independent.WaitForEntryAsync();
        await independent.ReleaseAsync(1);
        Assert.Equal(2, (await parallel.Execution).ExitCode);
        Assert.False(owning.Execution.IsCompleted);
        HostResult expired = await timedOut.Execution;
        Assert.Equal(0, expired.ExitCode);
        Assert.Contains("timed out", expired.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(waitingObservation + ".entered"));

        await owner.ReleaseAsync((byte)outcome);
        Assert.Equal(ownerExit, (await owning.Execution).ExitCode);
        List<int> later = [];
        for (int index = 0; index < expectedLater.Length; index++)
        {
            later.Add((await host.RunAsync(Arguments(), Json(fixture, "shared"), new HostSimulation("validation-failure", null))).ExitCode);
        }

        Assert.Equal(expectedLater, later);
    }

    /// <summary>Des hooks déjà entrés publient ensemble ; chaque invocation conserve son compteur et les entrées antérieures.</summary>
    /// <returns>Une tâche vérifiant les verdicts, la lecture concurrente et chaque compteur au plafond.</returns>
    [Fact]
    public async Task ConcurrentIdentityUpdatesPreserveEveryEntryAndRemainReadable()
    {
        using SolutionFixture fixture = new();
        ConsumerHost host = new(fixture.Root, instrument: false);
        File.WriteAllText(Path.Combine(fixture.Root, "compiled-host", "appsettings.json"),
            """{"HookRunner":{"StateFilePath":"Hooks/state.json","MaxBlockingRequests":1}}""");
        Assert.Equal(2, (await host.RunAsync(Arguments(), Json(fixture, "existing"), new HostSimulation("validation-failure", null))).ExitCode);
        await using HookGate first = new();
        await using HookGate second = new();
        await using HookGate third = new();
        await using RunningConsumer one = new(host, Arguments(), Json(fixture, "one"), new HostSimulation("gated", first.Name));
        await using RunningConsumer two = new(host, Arguments(), Json(fixture, "two"), new HostSimulation("gated", second.Name));
        await using RunningConsumer three = new(host, Arguments(), Json(fixture, "three"), new HostSimulation("gated", third.Name));
        await Task.WhenAll(first.WaitForEntryAsync(), second.WaitForEntryAsync(), third.WaitForEntryAsync());

        string snapshot = Path.Combine(fixture.Root, "published-snapshot.json");
        await FileSnapshot.CreateHardLinkAsync(snapshot, Path.Combine(fixture.Root, "Hooks", "state.json"), TestContext.Current.CancellationToken);
        string original = await File.ReadAllTextAsync(snapshot, TestContext.Current.CancellationToken);
        using JsonDocument initial = JsonDocument.Parse(original);
        string existingKey = Assert.Single(initial.RootElement.GetProperty("Counters").EnumerateObject()).Name;
        await StateFileObservation.VerifyAsync(Path.Combine(fixture.Root, "Hooks", "state.json"), async () =>
        {
            await Task.WhenAll(first.ReleaseAsync(1), second.ReleaseAsync(1), third.ReleaseAsync(1));
            await Task.WhenAll(one.Execution, two.Execution, three.Execution);
        }, document => AssertPublishedCounters(document, existingKey));
        HostResult[] results = await Task.WhenAll(one.Execution, two.Execution, three.Execution);
        Assert.All(results, result =>
        {
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("requested corrections", result.Error, StringComparison.Ordinal);
        });
        Assert.Equal(original, await File.ReadAllTextAsync(snapshot, TestContext.Current.CancellationToken));

        foreach (string session in new[] { "existing", "one", "two", "three" })
        {
            HostResult capped = await host.RunAsync(Arguments(), Json(fixture, session), new HostSimulation("validation-failure", null));
            Assert.Equal(0, capped.ExitCode);
            Assert.Contains("reached its correction request limit", capped.Error, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, "Hooks"), "*.tmp"));
    }

    /// <summary>Chaque lecture directe doit contenir un document entier et conserver l'entrée préexistante.</summary>
    /// <param name="document">Instantané lu au chemin publié sans verrou de stockage.</param>
    /// <param name="existingKey">Entrée présente avant toute autorisation d'écriture concurrente.</param>
    private static void AssertPublishedCounters(JsonElement document, string existingKey)
    {
        Assert.Equal(1, document.GetProperty("Version").GetInt32());
        JsonElement counters = document.GetProperty("Counters");
        Assert.Equal(1, counters.GetProperty(existingKey).GetInt32());
        JsonProperty[] entries = [.. counters.EnumerateObject()];
        Assert.InRange(entries.Length, 1, 4);
        Assert.All(entries, entry => Assert.Equal(1, entry.Value.GetInt32()));
    }

    /// <summary>Le verrou reste détenu pendant la confirmation, puis est libéré même si celle-ci échoue.</summary>
    /// <param name="confirmation">0 confirme l'arrêt, 1 simule son échec, 2 annule son délai technique distinct.</param>
    /// <returns>Une tâche vérifiant la possession du verrou et le compteur après confirmation.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task IdentityRemainsLockedUntilTerminationConfirmationAndIsReleasedAfterwards(int confirmation)
    {
        using SolutionFixture fixture = new();
        ConsumerHost host = new(fixture.Root, instrument: false);
        Assert.Equal(2, (await host.RunAsync(Arguments(), Json(fixture, "session-42"), new HostSimulation("validation-failure", null))).ExitCode);
        ManualTimeProvider clock = new();
        using StringWriter diagnostics = new();
        ConfirmationExecutor executor = new();
        Context context = new(executor, diagnostics, new FileHookStateStore(), clock);
        context.SetStrategy(new InspectSentinel(), fixture.Invocation,
            new HookOptions(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "Hooks", "state.json")));
        Task<int> supervising = context.ExecuteAsync(TestContext.Current.CancellationToken);
        await VerifyConfirmationAsync(fixture, host, clock, executor, supervising, confirmation);

        if (confirmation == 0)
        {
            Assert.Contains("termination confirmed", diagnostics.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Termination was not confirmed", diagnostics.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        }

        List<int> later = [];
        for (int index = 0; index < 3; index++)
        {
            later.Add((await host.RunAsync(Arguments(), Json(fixture, "session-42"), new HostSimulation("validation-failure", null))).ExitCode);
        }

        Assert.Equal([2, 2, 0], later);
    }

    /// <summary>Observe une confirmation suspendue en garantissant sa résolution après toute assertion.</summary>
    /// <param name="fixture">Solution et identité partagées par les deux superviseurs.</param>
    /// <param name="host">Consommateur réel qui tente la même identité.</param>
    /// <param name="clock">Horloge déclenchant l'arrêt du premier hook.</param>
    /// <param name="executor">Frontière d'exécution dont la confirmation est suspendue.</param>
    /// <param name="supervising">Invocation qui doit conserver le verrou jusqu'à la confirmation.</param>
    /// <param name="confirmation">Résultat de confirmation à restituer.</param>
    /// <returns>Une tâche terminée après assertions et nettoyage de la confirmation.</returns>
    private static async Task VerifyConfirmationAsync(SolutionFixture fixture, ConsumerHost host, ManualTimeProvider clock,
        ConfirmationExecutor executor, Task<int> supervising, int confirmation)
    {
        try
        {
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(120));
            await executor.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await using HookGate waiting = new();
            string waitingObservation = Path.Combine(fixture.Root, waiting.Name + ".json");
            HostResult expired = await host.RunAsync(Arguments(1), Json(fixture, "session-42", waitingObservation), new HostSimulation("gated", waiting.Name));
            Assert.Equal(0, expired.ExitCode);
            Assert.Contains("timed out", expired.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(waitingObservation + ".entered"));
            Assert.False(supervising.IsCompleted);
            executor.Complete(confirmation);
            int exit = await supervising.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(confirmation == 0 ? 0 : 2, exit);
        }
        finally
        {
            executor.Complete(1);
            await supervising.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    /// <summary>Contrôle l'arrêt à la frontière publique de l'exécution isolée, sans simuler les verrous.</summary>
    private sealed class ConfirmationExecutor : IIsolatedHookExecutor
    {
        private readonly TaskCompletionSource<HookExecutionResult> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken executionToken;

        /// <summary>Signal émis après acquisition du verrou par Context.</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Signal émis dès que l'échéance demande l'arrêt.</summary>
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public async Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            executionToken = cancellationToken;
            using CancellationTokenRegistration registration = cancellationToken.Register(() => Stopping.TrySetResult());
            Started.TrySetResult();
            return await completed.Task;
        }

        /// <summary>Résout la confirmation sans convertir son annulation technique en timeout confirmé.</summary>
        /// <param name="confirmation">0 confirme l'arrêt, 1 produit une panne, 2 une annulation technique.</param>
        public void Complete(int confirmation)
        {
            if (confirmation == 0)
            {
                completed.TrySetException(new OperationCanceledException(executionToken));
            }
            else if (confirmation == 1)
            {
                completed.TrySetException(new TimeoutException("Termination was not confirmed."));
            }
            else
            {
                completed.TrySetException(new OperationCanceledException("Termination was not confirmed.", new CancellationToken(true)));
            }
        }
    }

    /// <summary>Identifie le même hook que la CLI et interdit son exécution dans le superviseur.</summary>
    private sealed class InspectSentinel : HookStrategy
    {
        /// <inheritdoc />
        public override string Name => "inspect";

        /// <inheritdoc />
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The supervisor must not execute the strategy.");
    }

    /// <summary>Prépare une commande du hook inspect avec un budget global explicite.</summary>
    /// <param name="timeout">Budget en secondes, incluant l'attente du verrou.</param>
    /// <returns>Arguments publics à transmettre au consommateur.</returns>
    private static string[] Arguments(int timeout = 20)
        => ["execute", "inspect", "--agent", "codex", "--timeout", timeout.ToString(System.Globalization.CultureInfo.InvariantCulture)];

    /// <summary>Prépare un événement Codex dans la solution et la session du scénario.</summary>
    /// <param name="fixture">Solution contrôlée par le hook.</param>
    /// <param name="session">Session identifiant le compteur.</param>
    /// <param name="observation">Chemin propre à cette invocation, ou null pour le chemin de sa session.</param>
    /// <returns>Événement natif contenant le chemin de l'observation du hook.</returns>
    private static string Json(SolutionFixture fixture, string session, string? observation = null)
        => JsonSerializer.Serialize(new
        {
            session_id = session,
            hook_event_name = "Stop",
            cwd = fixture.Root,
            observation_path = observation ?? Path.Combine(fixture.Root, session + ".json"),
        });
}
