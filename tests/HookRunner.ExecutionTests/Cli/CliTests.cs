using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

/// <summary>Arguments, configuration et exécution du superviseur via l'hôte public.</summary>
[Collection("Console")]
public sealed class CliTests
{
    /// <summary>Une mauvaise configuration de plafond ou une surcharge d'état ne lance pas le hook.</summary>
    /// <param name="settings">Section HookRunner invalide.</param>
    /// <returns>Une tâche vérifiant diagnostic et absence d'exécution.</returns>
    [Theory]
    [InlineData("{\"StateFilePath\":\"Hooks/state.json\",\"MaxBlockingRequests\":-1}")]
    [InlineData("{\"StateFilePath\":\"Hooks/state.json\",\"MaxBlockingRequests\":\"invalid\"}")]
    [InlineData("{\"StateFilePath\":\"Hooks/state.json\",\"Hooks\":{\"hook\":{\"StateFilePath\":\"other.json\"}}}")]
    public async Task InvalidCounterConfigurationPreventsExecution(string settings)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("{\"HookRunner\":" + settings + "}");
        CapturingExecutor executor = new();
        int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"], services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.NotEmpty(console.Error.ToString());
        Assert.Null(executor.Request);
    }

    /// <summary>La configuration ne peut pas servir de fichier de compteurs.</summary>
    /// <returns>Une tâche vérifiant le refus et la conservation exacte de la configuration.</returns>
    [Fact]
    public async Task StateCannotReplaceTheConfigurationFile()
    {
        string solution = Path.Combine(AppContext.BaseDirectory, "StateConfigurationConflict.slnx");
        string content = "{\"HookRunner\":{\"StateFilePath\":\"appsettings.json\"}}";
        using ConfigurationFile configuration = new(content);
        using ConsoleCapture console = new(EventJson(AppContext.BaseDirectory));
        File.WriteAllText(solution, "<Solution />");
        try
        {
            int exit = await HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex"],
                services => services.AddSingleton<HookStrategy>(new ConsumerHook()), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, exit);
            Assert.Contains("distinct from appsettings.json", console.Error.ToString(), StringComparison.Ordinal);
            Assert.Equal(content, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        }
        finally
        {
            File.Delete(solution);
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
        foreach (string timeout in new[] { "", "0", "-1", "+1", " 1", "1 ", "1.5", "1e2", "text", "2147483648" })
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
    /// <param name="configuredTimeout">Valeur configurée, ou null pour le défaut de 120 secondes.</param>
    /// <param name="commandTimeout">Surcharge de commande facultative.</param>
    /// <param name="expectedTimeout">Budget effectif attendu dans les options de l'enfant.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData(null, null, 120)]
    [InlineData(37, null, 37)]
    [InlineData(37, "19", 19)]
    [InlineData(null, "1", 1)]
    [InlineData(null, "2147483647", int.MaxValue)]
    public async Task ConfigurationIsLoadedFromTheCompiledProgramAndTheResponseIsPropagated(
        int? configuredTimeout, string? commandTimeout, int expectedTimeout)
    {
        using SolutionFixture fixture = new();
        using ConsoleCapture console = new(EventJson(fixture.Root));
        string timeoutSetting = configuredTimeout.HasValue
            ? $",\"TimeoutSeconds\":{configuredTimeout.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : string.Empty;
        using ConfigurationFile configuration = new($"{{\"HookRunner\":{{\"StateFilePath\":\"Hooks/state.json\"{timeoutSetting}}}}}");
        CapturingExecutor executor = new();
        string[] arguments = ["execute", "hook", "--agent", "codex"];
        if (commandTimeout is not null)
        {
            arguments = [.. arguments, "--timeout", commandTimeout];
        }

        int exit = await HookRunnerHost.RunAsync(arguments, services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(expectedTimeout, executor.Request!.Options.TimeoutSeconds);
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

    /// <summary>Sérialise un événement Codex minimal valide pour les appels de l'hôte en mémoire.</summary>
    /// <param name="directory">Dossier de travail absolu et existant à inclure dans l'événement.</param>
    /// <returns>Le JSON contenant une session fixe, l'événement Stop et le dossier fourni.</returns>
    private static string EventJson(string directory) => JsonSerializer.Serialize(new
    {
        session_id = "session-42",
        hook_event_name = "Stop",
        cwd = directory,
    });
}
