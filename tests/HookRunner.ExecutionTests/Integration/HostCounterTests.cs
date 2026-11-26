using System.Text.Json;

namespace HookRunner.ExecutionTests;

/// <summary>Persistance et plafonds observés entre lancements de véritables consommateurs.</summary>
[Collection("Processes")]
public sealed class HostCounterTests
{
    /// <summary>Les mises à jour interprocessus ne perdent ni demandes partagées ni sessions indépendantes.</summary>
    /// <returns>Une tâche vérifiant les codes et le nettoyage des temporaires.</returns>
    [Fact]
    public async Task ConcurrentProcessesDoNotLoseRequestsOrOtherSessions()
    {
        using SolutionFixture fixture = new();
        // Ce scénario conserve sa concurrence réelle sans partager le fichier de hits interprocessus de Coverlet.
        // Les autres scénarios exécutent les mêmes assemblies avec instrumentation.
        ConsumerHost host = new(fixture.Root, instrument: false);
        /// <summary>Prépare un événement natif d'une session donnée.</summary>
        /// <param name="session">Session d'identité du compteur.</param>
        /// <returns>Entrée JSON à transmettre au consommateur.</returns>
        string Json(string session) => JsonSerializer.Serialize(new
        {
            session_id = session,
            hook_event_name = "Stop",
            cwd = fixture.Root,
            observation_path = Path.Combine(fixture.Root, "unused.json"),
        });
        Task<HostResult>[] requests = [.. Enumerable.Range(0, 8).Select(index => host.RunAsync(
            ["execute", "inspect", "--agent", "codex"], Json("shared"), new HostSimulation("validation-failure", null)))];
        Task<HostResult> independent = host.RunAsync(["execute", "inspect", "--agent", "codex"], Json("independent"),
            new HostSimulation("validation-failure", null));
        HostResult[] results = await Task.WhenAll(requests);
        Assert.Equal(3, results.Count(result => result.ExitCode == 2));
        Assert.Equal(5, results.Count(result => result.ExitCode == 0));
        Assert.Equal(2, (await independent).ExitCode);
        List<int> later = [];
        for (int index = 0; index < 3; index++)
        {
            later.Add((await host.RunAsync(["execute", "inspect", "--agent", "codex"], Json("independent"),
                new HostSimulation("validation-failure", null))).ExitCode);
        }

        Assert.Equal([2, 2, 0], later);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, "Hooks"), "*.tmp"));
    }

    /// <summary>Les plafonds globaux et par hook s'appliquent aussi aux événements informatifs.</summary>
    /// <param name="globalLimit">Plafond global configuré.</param>
    /// <param name="hookLimit">Surcharge facultative du hook.</param>
    /// <returns>Une tâche vérifiant la première et la deuxième demande.</returns>
    [Theory]
    [InlineData(1, null)]
    [InlineData(3, 1)]
    public async Task GlobalAndCaseInsensitiveHookLimitsAreApplied(int globalLimit, int? hookLimit)
    {
        using SolutionFixture fixture = new();
        ConsumerHost host = new(fixture.Root);
        string configuration = JsonSerializer.Serialize(new
        {
            HookRunner = new
            {
                StateFilePath = "Hooks/state.json",
                MaxBlockingRequests = globalLimit,
                Hooks = hookLimit is null ? []
                    : new Dictionary<string, object> { ["INSPECT"] = new { MaxBlockingRequests = hookLimit.Value } },
            },
        });
        File.WriteAllText(Path.Combine(fixture.Root, "compiled-host", "appsettings.json"), configuration);
        string json = JsonSerializer.Serialize(new
        {
            session_id = "limit-session",
            hook_event_name = "Notification",
            cwd = fixture.Root,
            observation_path = Path.Combine(fixture.Root, "observation.json"),
        });
        string[] arguments = ["execute", "inspect", "--agent", "codex"];
        HostResult first = await host.RunAsync(arguments, json, new HostSimulation("validation-failure", null));
        HostResult second = await host.RunAsync(arguments, json, new HostSimulation("validation-failure", null));

        Assert.Equal(2, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
    }

    /// <summary>Les demandes persistent entre programmes ; panne technique et plafond ne remettent pas à zéro.</summary>
    /// <returns>Une tâche vérifiant qu'un succès métier rétablit ensuite les demandes.</returns>
    [Fact]
    public async Task BusinessRequestsPersistAcrossProcessesAndSuccessResetsThem()
    {
        using SolutionFixture fixture = new();
        ConsumerHost host = new(fixture.Root);
        string json = JsonSerializer.Serialize(new
        {
            session_id = "counter-session",
            hook_event_name = "Stop",
            cwd = fixture.Root,
            observation_path = Path.Combine(fixture.Root, "observation.json"),
        });
        string[] arguments = ["execute", "inspect", "--agent", "codex"];
        List<int> exits = [];
        for (int index = 0; index < 4; index++)
        {
            HostResult result = await host.RunAsync(arguments, json, new HostSimulation("validation-failure", null));
            exits.Add(result.ExitCode);
        }

        Assert.Equal([2, 2, 2, 0], exits);
        HostResult failure = await host.RunAsync(arguments, json, new HostSimulation("technical-failure", null));
        Assert.Equal(2, failure.ExitCode);
        HostResult capped = await host.RunAsync(arguments, json, new HostSimulation("validation-failure", null));
        Assert.Equal(0, capped.ExitCode);
        HostResult success = await host.RunAsync(arguments, json);
        Assert.Equal(0, success.ExitCode);
        HostResult reset = await host.RunAsync(arguments, json, new HostSimulation("validation-failure", null));
        Assert.Equal(2, reset.ExitCode);
        Assert.Contains("Consumer correction required", reset.Error, StringComparison.Ordinal);
    }
}
