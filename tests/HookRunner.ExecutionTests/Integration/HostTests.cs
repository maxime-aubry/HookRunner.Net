using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios de bout en bout exécutant un véritable consommateur et son enfant.</summary>
[Collection("Processes")]
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

        ConsumerHost host = new(fixture.Root);
        HostResult result = await host.RunAsync(["execute", "inspect", "--agent", "codex"], json, new HostSimulation(mode, internalResult));

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

        ConsumerHost host = new(fixture.Root);
        HostResult result = await host.RunAsync(["execute", "inspect", "--agent", "codex"], json);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("HookRunner:", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// Vérifie en processus réels la sélection, la composition DI identique et la transmission des données
    /// préparées.
    /// </summary>
    /// <param name="useAppHost">True pour lancer l'apphost, false pour lancer l'assembly avec dotnet.</param>
    /// <param name="selectProjects">True vérifie la transmission de plusieurs projets à la stratégie enfant.</param>
    /// <returns>Une tâche représentant le scénario de test et ses assertions.</returns>
    /// <remarks>
    /// Une configuration contradictoire dans le dossier de lancement démontre que celle du programme compilé est
    /// utilisée.
    /// </remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConsumerExecutesItsOwnHookInAChildWithTheSameComposition(bool useAppHost, bool selectProjects)
    {
        using SolutionFixture fixture = new();
        string[] arguments = ["execute", "INSPECT", "--agent", "codex"];
        if (selectProjects)
        {
            File.WriteAllText(Path.Combine(fixture.Root, "Api.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(fixture.Root, "Worker.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"), """
                <Solution><Project Path="Api.csproj" /><Project Path="Worker.csproj" /></Solution>
                """);
            arguments = [.. arguments, "--projects", " api ,WORKER,Api"];
        }

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

        ConsumerHost host = new(fixture.Root, useAppHost);
        HostResult result = await host.RunAsync(arguments, json);

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
        if (selectProjects)
        {
            Assert.Equal(
            [
                new TargetProject("Api", Path.Combine(fixture.Root, "Api.csproj"), fixture.Root),
                new TargetProject("Worker", Path.Combine(fixture.Root, "Worker.csproj"), fixture.Root),
            ], options.Projects);
        }
        else
        {
            Assert.Empty(options.Projects);
        }
        Assert.True(File.Exists(options.StateFilePath));
    }

}
