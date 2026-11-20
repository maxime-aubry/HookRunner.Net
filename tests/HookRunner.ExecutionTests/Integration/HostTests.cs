using System.Diagnostics;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios de bout en bout exécutant un véritable consommateur et son enfant.</summary>
public sealed class HostTests
{
    /// <summary>
    /// Vérifie qu'un véritable hôte signale les erreurs enfant sans sortie parasite ni observation du hook.
    /// </summary>
    /// <param name="mode">Mode de panne demandé au consommateur de test.</param>
    /// <param name="internalResult">
    /// Résultat JSON simulé, ou null pour laisser le consommateur produire son résultat.
    /// </param>
    /// <param name="diagnostic">Fragment attendu sur stderr.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("unexpected-exit", null, "terminated unexpectedly")]
    [InlineData("technical-failure", null, "Consumer technical failure")]
    [InlineData("duplicate", null, "Duplicate hook")]
    [InlineData("invalid-result", "null", "Invalid hook child result")]
    [InlineData("invalid-result", "{}", "Hook execution failed")]
    [InlineData("invalid-result", "{\"Version\":1,\"Status\":99}", "Invalid hook child result")]
    [InlineData("invalid-result", "{\"Version\":1,\"Status\":2}", "Invalid hook child result")]
    [InlineData("invalid-result", "garbage", "Hook execution failed")]
    public async Task ChildFailuresAreDiagnosedOnStderr(string mode, string? internalResult, string diagnostic)
    {
        using SolutionFixture fixture = new();
        string observation = Path.Combine(fixture.Root, "observation.json");
        string json = JsonSerializer.Serialize(new
        {
            session_id = "session-42",
            hook_event_name = "Stop",
            cwd = fixture.Root,
            observation_path = observation,
        });

        HostResult result = await RunHostAsync(["execute", "inspect", "--agent", "codex"], json, fixture.Root, mode, internalResult);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(diagnostic, result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
        Assert.False(File.Exists(observation));
    }

    /// <summary>Vérifie qu'une entrée native invalide est refusée par l'hôte avant le lancement du hook.</summary>
    /// <param name="json">Entrée invalide envoyée sur stdin au véritable consommateur.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("garbage")]
    public async Task PreparationFailuresDoNotStartTheConsumerHook(string json)
    {
        using SolutionFixture fixture = new();

        HostResult result = await RunHostAsync(["execute", "inspect", "--agent", "codex"], json, fixture.Root);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("HookRunner:", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// Vérifie en processus réels la sélection, la composition DI identique et la transmission des données
    /// préparées.
    /// </summary>
    /// <param name="useAppHost">True pour lancer l'apphost, false pour lancer l'assembly avec dotnet.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    /// <remarks>
    /// Une configuration contradictoire dans le dossier de lancement démontre que celle du programme compilé est
    /// utilisée.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerExecutesItsOwnHookInAChildWithTheSameComposition(bool useAppHost)
    {
        using SolutionFixture fixture = new();
        string observationPath = Path.Combine(fixture.Root, "observation.json");
        string json = JsonSerializer.Serialize(new
        {
            session_id = "session-42",
            hook_event_name = "Stop",
            cwd = fixture.Invocation.WorkingDirectory,
            observation_path = observationPath,
        });
        // The launch directory has a conflicting configuration: only the compiled host's file applies.
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), "not valid JSON");

        HostResult result = await RunHostAsync(["execute", "INSPECT", "--agent", "codex"], json, fixture.Root, useAppHost: useAppHost);

        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.Error}");
        Assert.Empty(result.Output);
        Assert.Contains("succeeded", result.Error, StringComparison.Ordinal);
        using JsonDocument observation = JsonDocument.Parse(File.ReadAllText(observationPath));
        JsonElement root = observation.RootElement;
        Assert.NotEqual(result.ProcessId, root.GetProperty("ProcessId").GetInt32());
        Assert.Equal("consumer-service", root.GetProperty("Dependency").GetString());
        InvocationData invocation = root.GetProperty("Invocation").Deserialize<InvocationData>()!;
        Assert.Equal(json, invocation.OriginalJson);
        Assert.Equal("session-42", invocation.SessionId);
        HookOptions options = root.GetProperty("Options").Deserialize<HookOptions>()!;
        Assert.Equal(37, options.TimeoutSeconds);
        Assert.Equal(fixture.Root, options.SolutionRoot);
        Assert.Equal(fixture.Root, options.DefaultWorkingDirectory);
        Assert.Empty(options.Projects);
        Assert.False(File.Exists(options.StateFilePath));
    }

    /// <summary>Lance une copie isolée du consommateur, lui transmet son entrée et capture ses flux.</summary>
    /// <param name="arguments">Arguments applicatifs à transmettre après la commande de lancement.</param>
    /// <param name="json">Texte envoyé sur stdin avant fermeture du flux.</param>
    /// <param name="directory">Dossier de lancement et parent de la copie compiled-host.</param>
    /// <param name="mode">Mode de simulation transmis par variable d'environnement, ou null.</param>
    /// <param name="internalResult">Contenu du résultat simulé, ou null pour utiliser le protocole normal.</param>
    /// <param name="useAppHost">Choisit l'apphost natif plutôt que dotnet.</param>
    /// <returns>Une tâche donnant le PID, le code de sortie, stdout et stderr du superviseur.</returns>
    /// <remarks>
    /// L'attente est bornée à trente secondes. Un processus encore actif est arrêté dans le nettoyage.
    /// </remarks>
    private static async Task<HostResult> RunHostAsync(string[] arguments, string json, string directory, string? mode = null, string? internalResult = null, bool useAppHost = false)
    {
        ChildProcessCommand command = ConsumerProgram.CopyTo(Path.Combine(directory, "compiled-host"), useAppHost);
        ProcessStartInfo start = new(command.FileName)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (mode is not null)
        {
            start.Environment["HOOKRUNNER_TEST_MODE"] = mode;
        }

        if (internalResult is not null)
        {
            start.Environment["HOOKRUNNER_TEST_RESULT"] = internalResult;
        }
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(json.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return new HostResult(process.Id, process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>Flux, code de sortie et identifiant du processus superviseur observé par un test.</summary>
    /// <param name="ProcessId">Identifiant du processus observé pendant le scénario d'intégration.</param>
    /// <param name="ExitCode">Code de sortie à restituer au programme appelant.</param>
    /// <param name="Output">Texte capturé sur la sortie standard du processus observé.</param>
    /// <param name="Error">Tampon contenant les diagnostics écrits sur stderr pendant le test.</param>
    private sealed record HostResult(int ProcessId, int ExitCode, string Output, string Error);
}
