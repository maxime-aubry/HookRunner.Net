using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios de l'API d'hébergement avec capture des flux et configuration temporaire.</summary>
[Collection("Console")]
public sealed class CliTests
{
    /// <summary>
    /// Vérifie la libération asynchrone des services du superviseur et le diagnostic d'une erreur de libération.
    /// </summary>
    /// <param name="failsDuringDisposal">
    /// True pour simuler une exception pendant la libération et attendre le code 2.
    /// </param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostDisposesItsComposedServicesAndReportsDisposalFailures(bool failsDuringDisposal)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":{\"StateFilePath\":\"Hooks/state.json\"}}");
        AsyncConsumerService? dependency = null;

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"], services =>
        {
            services.AddSingleton(_ => new AsyncConsumerService { FailsOnDispose = failsDuringDisposal });
            services.AddSingleton<HookStrategy>(provider =>
            {
                dependency = provider.GetRequiredService<AsyncConsumerService>();
                return new ConsumerHook();
            });
            services.AddSingleton<IIsolatedHookExecutor>(new CapturingExecutor());
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(failsDuringDisposal ? 2 : 0, exit);
        Assert.True(dependency!.Disposed);
        if (failsDuringDisposal)
        {
            Assert.Contains("consumer disposal failure", console.Error.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Vérifie qu'une sélection de projets encore non supportée échoue avant d'atteindre l'exécuteur.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>
    /// Vérifie qu'un exécutable enfant inexistant produit le code 2 et un diagnostic contenant son chemin.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task HostReportsFailureToStartTheConfiguredChild()
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":{\"StateFilePath\":\"Hooks/state.json\"}}");
        string missingExecutable = Path.Combine(fixture.Root, "missing-child-executable");
        ChildProcessCommand childProcess = new(missingExecutable, []);

        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"],
            services => services.AddSingleton<HookStrategy>(new ConsumerHook()), childProcess, TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains(missingExecutable, console.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Fournit les commandes invalides couvrant noms, options, agent et valeurs de timeout.</summary>
    /// <returns>Les lignes de données de théorie contenant chacune un tableau d'arguments invalide.</returns>
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

    /// <summary>Vérifie qu'une commande invalide renvoie 2 avec un diagnostic de préparation.</summary>
    /// <param name="arguments">Commande invalide fournie par la source de données de théorie.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidCommandsReturnTwoWithDiagnostics(string[] arguments)
    {
        using ConsoleCapture console = new("{}");

        int exit = await HookRunnerHost.RunAsync(arguments, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("HookRunner:", console.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Vérifie le chargement de la configuration du programme compilé, la surcharge du timeout et la propagation
    /// du code applicatif.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>Vérifie qu'une configuration invalide produit un diagnostic sans appeler l'exécuteur.</summary>
    /// <param name="settings">Contenu temporaire d'appsettings.json à rendre invalide.</param>
    /// <param name="diagnostic">Fragment attendu dans le diagnostic écrit sur stderr.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("{}", "StateFilePath")]
    [InlineData("{\"HookRunner\":{\"StateFilePath\":\"/state.json\"}}", "StateFilePath")]
    [InlineData("{\"HookRunner\":{\"StateFilePath\":\"../state.json\"}}", "StateFilePath")]
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

    /// <summary>
    /// Vérifie que l'enfant exécute directement les données préparées malgré une entrée et une configuration
    /// illisibles.
    /// </summary>
    /// <param name="fails">
    /// True pour simuler une exception de stratégie et vérifier son résultat technique.
    /// </param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
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

    /// <summary>
    /// Vérifie qu'une requête enfant mal formée produit un résultat technique sans exécuter le hook.
    /// </summary>
    /// <param name="json">Contenu invalide à écrire dans le fichier de requête enfant.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("invalid JSON")]
    [InlineData("{\"HookName\":\"hook\",\"Invocation\":null,\"Options\":null}")]
    public async Task ChildRejectsInvalidPreparedRequest(string json)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new("");
        string input = Path.Combine(fixture.Root, "request.json");
        string output = Path.Combine(fixture.Root, "result.json");
        File.WriteAllText(input, json);
        ConsumerHook hook = new();

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command, input, output], services => services.AddSingleton<HookStrategy>(hook),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        ChildHookResult result = JsonSerializer.Deserialize<ChildHookResult>(File.ReadAllText(output))!;
        Assert.Equal(HookExecutionStatus.TechnicalFailure, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.False(hook.Executed);
    }

    /// <summary>
    /// Vérifie la libération des dépendances injectées dans l'enfant après succès ou exception du hook.
    /// </summary>
    /// <param name="fails">True pour faire échouer le hook après résolution de sa dépendance.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildDisposesItsDependenciesAfterSuccessOrFailure(bool fails)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new("");
        HookOptions options = new(120, fixture.Root, fixture.Root, [], "prepared-state");
        IsolatedHookRequest request = new("hook", fixture.Invocation, options);
        string input = Path.Combine(fixture.Root, "request.json");
        string output = Path.Combine(fixture.Root, "result.json");
        File.WriteAllText(input, JsonSerializer.Serialize(request));
        AsyncConsumerService? dependency = null;

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command, input, output], services =>
        {
            services.AddSingleton<AsyncConsumerService>();
            services.AddTransient<HookStrategy>(provider =>
            {
                dependency = provider.GetRequiredService<AsyncConsumerService>();
                return new ConsumerHook { Fails = fails };
            });
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(dependency!.Disposed);
        ChildHookResult result = JsonSerializer.Deserialize<ChildHookResult>(File.ReadAllText(output))!;
        Assert.Equal(fails ? HookExecutionStatus.TechnicalFailure : HookExecutionStatus.Success, result.Status);
    }

    /// <summary>
    /// Vérifie qu'un résultat enfant impossible à écrire donne le code 2 après l'exécution du hook.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task ChildResultWriteFailureReturnsTwoWithDiagnostics()
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new("");
        HookOptions options = new(120, fixture.Root, fixture.Root, [], "prepared-state");
        IsolatedHookRequest request = new("hook", fixture.Invocation, options);
        string input = Path.Combine(fixture.Root, "request.json");
        File.WriteAllText(input, JsonSerializer.Serialize(request));
        ConsumerHook hook = new();

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command, input, fixture.Root],
            services => services.AddSingleton<HookStrategy>(hook), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(hook.Executed);
        Assert.Equal(2, exit);
        Assert.Contains("HookRunner:", console.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Vérifie qu'un marqueur enfant sans chemins de transport produit un diagnostic et le code 2.
    /// </summary>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Fact]
    public async Task InvalidChildArgumentsReturnTwo()
    {
        using ConsoleCapture console = new("");

        int exit = await HookRunnerHost.RunAsync([ChildHookProtocol.Command], _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("Invalid hook child arguments", console.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Sérialise un événement Codex minimal valide pour les appels de l'hôte en mémoire.</summary>
    /// <param name="directory">Dossier de travail absolu et existant à inclure dans l'événement.</param>
    /// <returns>Le JSON contenant une session fixe, l'événement Stop et le dossier fourni.</returns>
    private static string EventJson(string directory) => JsonSerializer.Serialize(new
    {
        session_id = "session-42",
        hook_event_name = "Stop",
        cwd = directory,
    });

    /// <summary>Stratégie de test mémorisant ses entrées et permettant de simuler un échec.</summary>
    private sealed class ConsumerHook : HookStrategy
    {
        /// <summary>Nom du hook ou du projet cible représenté par ce type.</summary>
        public override string Name => "hook";
        /// <summary>Indique si le hook de test doit renvoyer une tâche en échec.</summary>
        public bool Fails { get; init; }
        /// <summary>Indique si la stratégie de test a effectivement été appelée.</summary>
        public bool Executed { get; private set; }
        /// <summary>Données neutres de l'événement déclencheur, avec le JSON original.</summary>
        public InvocationData? Invocation { get; private set; }
        /// <summary>Options préparées pour cette invocation du hook.</summary>
        public HookOptions? Options { get; private set; }

        /// <summary>Mémorise les données reçues puis simule un succès ou un échec selon Fails.</summary>
        /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
        /// <param name="options">Options préparées pour cette invocation du hook.</param>
        /// <param name="cancellationToken">
        /// Token d'annulation transmis aux opérations qui le prennent en charge.
        /// </param>
        /// <returns>Une tâche terminée ou en échec avec le message consumer failure.</returns>
        /// <remarks>
        /// Aucun processus n'est lancé et le token n'est pas observé par ce double déterministe.
        /// </remarks>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
        {
            Executed = true;
            Invocation = invocation;
            Options = options;
            return Fails ? Task.FromException(new InvalidOperationException("consumer failure")) : Task.CompletedTask;
        }
    }

    /// <summary>Double d'exécution mémorisant la requête reçue et renvoyant un succès.</summary>
    private sealed class CapturingExecutor : IIsolatedHookExecutor
    {
        /// <summary>
        /// Dernière requête mémorisée par le double d'exécution, ou null avant le premier appel.
        /// </summary>
        public IsolatedHookRequest? Request { get; private set; }

        /// <summary>Mémorise la requête isolée reçue sans démarrer de processus.</summary>
        /// <param name="request">
        /// Requête isolée contenant le nom du hook, l'invocation et les options préparées.
        /// </param>
        /// <param name="cancellationToken">
        /// Token d'annulation transmis aux opérations qui le prennent en charge.
        /// </param>
        /// <returns>Une tâche déjà terminée contenant un succès sans diagnostic.</returns>
        /// <remarks>Le token est accepté pour respecter le contrat, sans attente ni opération annulable.</remarks>
        public Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
        }
    }

    /// <summary>Dépendance jetable permettant d'observer la libération asynchrone du conteneur.</summary>
    private sealed class AsyncConsumerService : IAsyncDisposable
    {
        /// <summary>Indique si la dépendance de test doit échouer lors de sa libération.</summary>
        public bool FailsOnDispose { get; init; }
        /// <summary>Indique si la libération de la dépendance a été exécutée.</summary>
        public bool Disposed { get; private set; }

        /// <summary>Simule une libération asynchrone et mémorise son exécution, même lorsqu'elle échoue.</summary>
        /// <returns>Une ValueTask qui termine normalement ou lève l'erreur de libération simulée.</returns>
        /// <exception cref="InvalidOperationException">
        /// FailsOnDispose demande une erreur de libération.
        /// </exception>
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposed = true;
            if (FailsOnDispose)
            {
                throw new InvalidOperationException("consumer disposal failure");
            }
        }
    }

    /// <summary>Sauvegarde temporaire de la configuration du programme de tests.</summary>
    private sealed class ConfigurationFile : IDisposable
    {
        private readonly string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private readonly string? previous;

        /// <summary>
        /// Sauvegarde l'éventuelle configuration du programme de tests puis installe un contenu temporaire.
        /// </summary>
        /// <param name="json">Contenu à écrire dans appsettings.json à côté du programme compilé.</param>
        /// <remarks>
        /// Le fichier existant sera restauré lors de Dispose ; les tests utilisant cette ressource sont
        /// sérialisés.
        /// </remarks>
        public ConfigurationFile(string json)
        {
            previous = File.Exists(path) ? File.ReadAllText(path) : null;
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Restaure le fichier de configuration précédent, ou supprime le fichier temporaire s'il n'existait pas.
        /// </summary>
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

    /// <summary>Capture jetable des flux globaux d'entrée et de diagnostic pour les tests sérialisés.</summary>
    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextReader previousInput = Console.In;
        private readonly TextWriter previousError = Console.Error;
        private readonly StringReader input;
        /// <summary>Tampon contenant les diagnostics écrits sur stderr pendant le test.</summary>
        public StringWriter Error { get; } = new();

        /// <summary>
        /// Remplace stdin par une chaîne et stderr par un tampon en mémorisant les flux précédents.
        /// </summary>
        /// <param name="json">Texte présenté au code testé comme entrée standard.</param>
        /// <remarks>
        /// Les flux globaux doivent être restaurés par Dispose ; les tests concernés s'exécutent sans
        /// parallélisme.
        /// </remarks>
        public ConsoleCapture(string json)
        {
            input = new StringReader(json);
            Console.SetIn(input);
            Console.SetError(Error);
        }

        /// <summary>
        /// Restaure les flux standard précédents puis libère le lecteur et le tampon de diagnostic temporaires.
        /// </summary>
        public void Dispose()
        {
            Console.SetIn(previousInput);
            Console.SetError(previousError);
            input.Dispose();
            Error.Dispose();
        }
    }
}
