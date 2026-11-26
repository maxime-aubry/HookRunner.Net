using HookRunner.Application;
using HookRunner.Domain;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Contrats de compteur observés via le cas d'usage et le stockage réel.</summary>
public sealed class CounterTests
{
    /// <summary>La casse du préfixe de lecteur Windows n'introduit pas une nouvelle identité.</summary>
    /// <returns>Une tâche vérifiant le partage du compteur pour les deux graphies du même lecteur.</returns>
    [Fact]
    public async Task RootPrefixCaseDoesNotChangeTheIdentity()
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        string alias = char.ToLowerInvariant(fixture.Root[0]) + fixture.Root[1..];
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        Assert.Equal(0, await RunPreparedAsync(options with { SolutionRoot = alias }, fixture.Invocation));
    }

    /// <summary>Les alias de casse partagent leur compteur uniquement lorsque le volume les confond.</summary>
    /// <returns>Une tâche vérifiant le comportement réel du système de fichiers, y compris macOS.</returns>
    [Fact]
    public async Task RootCaseAliasesFollowTheVolumeSemantics()
    {
        using SolutionFixture fixture = new();
        string root = Path.Combine(fixture.Root, "CaseRoot");
        string alias = Path.Combine(fixture.Root, "caseroot");
        Directory.CreateDirectory(root);
        bool sameDirectory = Directory.Exists(alias);
        Directory.CreateDirectory(alias);
        HookOptions options = new(120, root, root, [], Path.Combine(fixture.Root, "state.json"), 1);

        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        Assert.Equal(sameDirectory ? 0 : 2, await RunPreparedAsync(options with { SolutionRoot = alias }, fixture.Invocation));
    }

    /// <summary>Une ouverture achevée après l'échéance ne démarre pas le hook, même si le rappel de timer est retardé.</summary>
    /// <returns>Une tâche vérifiant le contrôle monotone après acquisition.</returns>
    [Fact]
    public async Task ExpirationDuringStatePreparationPreventsALateStart()
    {
        using SolutionFixture fixture = new();
        ManualTimeProvider clock = new();
        ResultExecutor executor = new(HookExecutionStatus.Success);
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new ConsumerHook()], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(executor, diagnostics, new DelayedStateStore(clock), clock));
        RunHookResponse result = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("hook", null, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(executor.Executed);
        Assert.Contains("timed out", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Simule un temps d'accès disque dépassant l'échéance sans rappel de timer.</summary>
    /// <param name="clock">Horloge monotone contrôlée.</param>
    private sealed class DelayedStateStore(ManualTimeProvider clock) : IHookStateStore
    {
        /// <inheritdoc />
        public async Task<IHookStateSession> OpenAsync(string stateFilePath, HookIdentity identity, CancellationToken cancellationToken)
        {
            IHookStateSession session = await new FileHookStateStore().OpenAsync(stateFilePath, identity, cancellationToken);
            clock.Advance(TimeSpan.FromSeconds(120), dispatchTimers: false);
            return session;
        }
    }

    /// <summary>Un timeout pendant la sérialisation n'exécute pas le hook et conserve le compteur.</summary>
    /// <returns>Une tâche validant le résultat et la prochaine demande.</returns>
    [Fact]
    public async Task TimeoutWhileWaitingForTheIdentityDoesNotStartTheHookOrChangeTheState()
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        FileHookStateStore store = new();
        HookIdentity identity = new(fixture.Root, "codex", fixture.Invocation.SessionId, "hook");
        await using (IHookStateSession held = await store.OpenAsync(options.StateFilePath, identity, TestContext.Current.CancellationToken))
        {
            ManualTimeProvider clock = new();
            ResultExecutor executor = new(HookExecutionStatus.Success);
            using StringWriter diagnostics = new();
            RunHookUseCase useCase = new([new ConsumerHook()], new PreparedOptions(options), new Context(executor, diagnostics, store, clock));
            Task<RunHookResponse> waiting = useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("hook", null, []), fixture.Invocation),
                TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(120));
            RunHookResponse response = await waiting;
            Assert.Equal(0, response.ExitCode);
            Assert.False(executor.Executed);
            Assert.Equal(1, held.Count);
        }

        Assert.Equal(0, await RunPreparedAsync(options, fixture.Invocation));
    }

    /// <summary>Un chemin occupé par un dossier provoque une erreur d'accès avant le hook.</summary>
    /// <returns>Une tâche vérifiant l'absence d'exécution et le diagnostic.</returns>
    [Fact]
    public async Task AnInaccessibleStatePathPreventsTheHookFromStarting()
    {
        using SolutionFixture fixture = new();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "Hooks", "state.json"));
        ResultExecutor executor = new(HookExecutionStatus.Success);
        using StringWriter diagnostics = new();
        RunHookResponse result = await CreateUseCase(executor, diagnostics).ExecuteAsync(
            new RunHookRequest(new RunHookArgs("hook", null, []), fixture.Invocation), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.False(executor.Executed);
        Assert.Contains("Hook execution failed", diagnostics.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Le code zéro d'un timeout ne peut pas être interprété comme un succès métier.</summary>
    /// <returns>Une tâche vérifiant que le prochain verdict reste au plafond.</returns>
    [Fact]
    public async Task AZeroReturnedForTimeoutDoesNotResetTheCounter()
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        ManualTimeProvider clock = new();
        GateExecutor executor = new(token =>
        {
            clock.Advance(TimeSpan.FromSeconds(120));
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        });
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new ConsumerHook()], new PreparedOptions(options),
            new Context(executor, diagnostics, new FileHookStateStore(), clock));
        RunHookResponse timeout = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs("hook", null, []), fixture.Invocation),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, timeout.ExitCode);
        Assert.Contains("timed out", diagnostics.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await RunPreparedAsync(options, fixture.Invocation));
    }

    /// <summary>Les chemins physiques équivalents partagent leur compteur.</summary>
    /// <returns>Une tâche validant la casse Windows ou les liens symboliques Unix.</returns>
    [Fact]
    public async Task CanonicalRootAliasesShareTheCounter()
    {
        using SolutionFixture fixture = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        string alias = Path.Combine(fixture.Root, "alias");
        using DirectoryLink link = new(alias, fixture.Root);
        Assert.Equal(0, await RunPreparedAsync(options with { SolutionRoot = alias }, fixture.Invocation));
    }

    /// <summary>Événement et options sont exclus de l'identité ; agents, sessions, hooks et worktrees restent distincts.</summary>
    /// <returns>Une tâche validant le partage et l'isolation des compteurs.</returns>
    [Fact]
    public async Task OnlyRootAgentSessionAndCaseInsensitiveHookNameDefineTheIdentity()
    {
        using SolutionFixture fixture = new();
        using SolutionFixture worktree = new();
        HookOptions options = new(120, fixture.Root, fixture.Root, [], Path.Combine(fixture.Root, "state.json"), 1);
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation));
        InvocationData changed = fixture.Invocation with { EventName = "Notification", Project = "Other", OriginalJson = "{}" };
        HookOptions changedOptions = options with
        {
            TimeoutSeconds = 17,
            Projects = [new TargetProject("Target", Path.Combine(fixture.Root, "Target.csproj"), fixture.Root)],
        };
        Assert.Equal(0, await RunPreparedAsync(changedOptions, changed, "HOOK"));
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation with { Agent = "claude" }));
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation with { SessionId = "another" }));
        Assert.Equal(2, await RunPreparedAsync(options, fixture.Invocation, "another-hook"));
        Assert.Equal(2, await RunPreparedAsync(options with { SolutionRoot = worktree.Root }, fixture.Invocation));
    }

    /// <summary>Exécute une demande métier avec des options déjà préparées pour varier l'identité indépendamment de la résolution.</summary>
    /// <param name="options">Options de l'invocation.</param>
    /// <param name="invocation">Données neutres d'identité et d'événement.</param>
    /// <param name="name">Nom demandé et enregistré du hook.</param>
    /// <returns>Code public de la demande de correction.</returns>
    private static async Task<int> RunPreparedAsync(HookOptions options, InvocationData invocation, string name = "hook")
    {
        using StringWriter diagnostics = new();
        RunHookUseCase useCase = new([new NamedHook(name)], new PreparedOptions(options),
            new Context(new ResultExecutor(HookExecutionStatus.ValidationFailure), diagnostics, new FileHookStateStore()));
        RunHookResponse response = await useCase.ExecuteAsync(new RunHookRequest(new RunHookArgs(name, null, []), invocation),
            TestContext.Current.CancellationToken);
        return response.ExitCode;
    }

    /// <summary>Frontière de préparation contrôlée pour les scénarios d'identité.</summary>
    /// <param name="options">Options à remettre au cas d'usage.</param>
    private sealed class PreparedOptions(HookOptions options) : IHookOptionsPreparer
    {
        /// <inheritdoc />
        public HookOptions Prepare(RunHookArgs args, InvocationData invocation) => options;
    }

    /// <summary>Identifie une stratégie qui doit rester isolée.</summary>
    /// <param name="name">Nom du hook enregistré.</param>
    private sealed class NamedHook(string name) : HookStrategy
    {
        public override string Name => name;
        /// <summary>Signale toute exécution accidentelle dans le superviseur.</summary>
        /// <param name="invocation">Données de l'événement.</param>
        /// <param name="options">Options préparées.</param>
        /// <param name="cancellationToken">Budget d'exécution.</param>
        /// <returns>Ne retourne jamais.</returns>
        /// <exception cref="InvalidOperationException">La stratégie ne doit pas être exécutée ici.</exception>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Only the isolated executor runs the hook.");
    }

    /// <summary>La corruption ne doit jamais être remplacée par un état vide.</summary>
    /// <param name="content">Document corrompu à conserver exactement.</param>
    /// <returns>Une tâche vérifiant diagnostic, conservation et absence d'exécution.</returns>
    [Theory]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":1,\"Counters\":null}")]
    [InlineData("{\"Version\":1,\"Version\":1,\"Counters\":{}}")]
    [InlineData("{\"Version\":1,\"Counters\":{\"bad\":-1}}")]
    public async Task CorruptStateIsDiagnosedAndNeverReplaced(string content)
    {
        using SolutionFixture fixture = new();
        string path = Path.Combine(fixture.Root, "Hooks", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        ResultExecutor executor = new(HookExecutionStatus.Success);
        using StringWriter diagnostics = new();

        RunHookResponse result = await CreateUseCase(executor, diagnostics).ExecuteAsync(
            new RunHookRequest(new RunHookArgs("hook", null, []), fixture.Invocation), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
        Assert.False(executor.Executed);
        Assert.NotEmpty(diagnostics.ToString());
        Assert.Equal(content, File.ReadAllText(path));
    }

    /// <summary>Le verrou sérialise le hook complet, sans empêcher une session indépendante d'avancer.</summary>
    /// <returns>Une tâche validant l'absence de chevauchement et de demandes perdues.</returns>
    [Fact]
    public async Task AnIdentityIsHeldForTheEntireHookWhileOtherIdentitiesCanRun()
    {
        using SolutionFixture fixture = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource waitingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GateExecutor firstExecutor = new(async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return new HookExecutionResult(HookExecutionStatus.ValidationFailure, "first");
        });
        using StringWriter firstDiagnostics = new();
        RunHookUseCase first = CreateUseCase(firstExecutor, firstDiagnostics);
        RunHookRequest request = new(new RunHookArgs("hook", null, []), fixture.Invocation);
        Task<RunHookResponse> running = first.ExecuteAsync(request, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        GateExecutor secondExecutor = new(token =>
        {
            waitingStarted.SetResult();
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.ValidationFailure, "second"));
        });
        using StringWriter secondDiagnostics = new();
        Task<RunHookResponse> waiting = CreateUseCase(secondExecutor, secondDiagnostics)
            .ExecuteAsync(request, TestContext.Current.CancellationToken);
        bool overlapped = waitingStarted.Task.IsCompleted;
        await AssertIndependentInvocationAsync(fixture, release, running, waiting);

        Assert.False(overlapped);
        Assert.True(waitingStarted.Task.IsCompleted);
        (int third, _) = await RunAsync(fixture, HookExecutionStatus.ValidationFailure);
        (int fourth, _) = await RunAsync(fixture, HookExecutionStatus.ValidationFailure);
        Assert.Equal(2, third);
        Assert.Equal(0, fourth);
    }

    /// <summary>Vérifie une identité indépendante puis libère et attend les deux invocations concurrentes.</summary>
    /// <param name="fixture">Solution et invocation de référence.</param>
    /// <param name="release">Signal de fin de la première invocation.</param>
    /// <param name="running">Première invocation maintenant le verrou.</param>
    /// <param name="waiting">Deuxième invocation attendant la même identité.</param>
    /// <returns>Une tâche terminée après les assertions et la fin des deux invocations.</returns>
    private static async Task AssertIndependentInvocationAsync(
        SolutionFixture fixture, TaskCompletionSource release, Task<RunHookResponse> running, Task<RunHookResponse> waiting)
    {
        try
        {
            (int independent, _) = await RunAsync(fixture, HookExecutionStatus.ValidationFailure,
                fixture.Invocation with { SessionId = "independent" });
            Assert.Equal(2, independent);
        }
        finally
        {
            release.SetResult();
            await running;
            await waiting;
        }

    }

    /// <summary>Les trois premières demandes bloquent, puis le hook continue avec une trace de plafond.</summary>
    /// <returns>Une tâche vérifiant codes et diagnostics à travers de nouveaux contextes.</returns>
    [Fact]
    public async Task ThreeRequestsBlockAndLaterRequestsStillExecuteWithoutBlocking()
    {
        using SolutionFixture fixture = new();
        List<int> exits = [];
        for (int index = 0; index < 5; index++)
        {
            (int exit, string diagnostic) = await RunAsync(fixture, HookExecutionStatus.ValidationFailure);
            exits.Add(exit);
            if (index >= 3)
            {
                Assert.Contains("limit", diagnostic, StringComparison.Ordinal);
                Assert.DoesNotContain("Fix the violation", diagnostic, StringComparison.Ordinal);
            }
        }

        Assert.Equal([2, 2, 2, 0, 0], exits);
    }

    /// <summary>Exécute une nouvelle invocation utilisant le stockage réel de la fixture.</summary>
    /// <param name="fixture">Solution temporaire.</param>
    /// <param name="status">Verdict isolé à simuler.</param>
    /// <param name="invocation">Identité facultative remplaçant celle de la fixture.</param>
    /// <returns>Code de sortie et diagnostics capturés.</returns>
    private static async Task<(int Exit, string Diagnostic)> RunAsync(SolutionFixture fixture, HookExecutionStatus status,
        InvocationData? invocation = null)
    {
        using StringWriter diagnostics = new();
        ResultExecutor executor = new(status);
        RunHookUseCase useCase = CreateUseCase(executor, diagnostics);
        RunHookResponse response = await useCase.ExecuteAsync(
            new RunHookRequest(new RunHookArgs("hook", null, []), invocation ?? fixture.Invocation), TestContext.Current.CancellationToken);
        Assert.True(executor.Executed, diagnostics.ToString());
        return (response.ExitCode, diagnostics.ToString());
    }

    /// <summary>Compose le cas d'usage avec préparation et stockage de production.</summary>
    /// <param name="executor">Simulation de la frontière d'exécution isolée.</param>
    /// <param name="diagnostics">Writer capturant les diagnostics.</param>
    /// <returns>Nouveau cas d'usage indépendant.</returns>
    private static RunHookUseCase CreateUseCase(IIsolatedHookExecutor executor, TextWriter diagnostics)
        => new([new ConsumerHook()], new HookOptionsPreparer(120, "Hooks/state.json"),
            new Context(executor, diagnostics, new FileHookStateStore()));

    /// <summary>Frontière d'exécution contrôlée par le scénario.</summary>
    /// <param name="execute">Comportement asynchrone de l'exécution.</param>
    private sealed class GateExecutor(Func<CancellationToken, Task<HookExecutionResult>> execute) : IIsolatedHookExecutor
    {
        /// <inheritdoc />
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
            => execute(cancellationToken);
    }

    /// <summary>Simule un verdict métier ou technique et expose si le hook a été démarré.</summary>
    /// <param name="status">Verdict à restituer.</param>
    private sealed class ResultExecutor(HookExecutionStatus status) : IIsolatedHookExecutor
    {
        public bool Executed { get; private set; }

        /// <inheritdoc />
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(new HookExecutionResult(status, "Fix the violation"));
        }
    }
}
