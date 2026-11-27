using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Domain;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

/// <summary>Garanties de sélection observées à travers l'hôte public et son état persistant.</summary>
[Collection("Console")]
public sealed class ProjectSelectionHostTests
{
    /// <summary>Une liste de plusieurs projets prépare une seule invocation, sans doublon.</summary>
    /// <returns>Une tâche vérifiant les options remises à l'exécuteur isolé.</returns>
    [Fact]
    public async Task MultipleProjectsProduceOneExecution()
    {
        using SolutionFixture fixture = new();
        WriteProjects(fixture.Root);
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("""
            {"HookRunner":{"StateFilePath":"Hooks/state.json"}}
            """);
        CapturingExecutor executor = new();

        int exit = await RunAsync(" api ,Worker,API ", executor);

        Assert.Equal(0, exit);
        Assert.Equal(1, executor.ExecutionCount);
        Assert.Equal(["Api", "Worker"], executor.Request!.Options.Projects.Select(project => project.Name));
        Assert.All(executor.Request.Options.Projects, project =>
        {
            Assert.True(Path.IsPathFullyQualified(project.ProjectFilePath));
            Assert.Equal(Path.GetDirectoryName(project.ProjectFilePath), project.DirectoryPath);
        });
    }

    /// <summary>Une liste invalide ne démarre rien et ne crée ni ne modifie le fichier de compteurs.</summary>
    /// <param name="selection">Valeur de --projects explicitement invalide.</param>
    /// <returns>Une tâche vérifiant d'abord un état absent, puis un compteur existant.</returns>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData("Api,")]
    [InlineData(",Api")]
    [InlineData("Api,,Worker")]
    [InlineData("Api, ")]
    [InlineData("Api,Missing")]
    [InlineData("Api,Unreferenced")]
    [InlineData("Api,VisualBasic")]
    [InlineData("Api,Ap")]
    [InlineData("Api,*.csproj")]
    [InlineData("Api,src/Worker")]
    public async Task InvalidSelectionPreservesAbsentAndExistingState(string selection)
    {
        using SolutionFixture fixture = new();
        WriteProjects(fixture.Root);
        using ConsoleCapture console = new(EventJson(fixture.Root));
        using ConfigurationFile configuration = new("""
            {"HookRunner":{"StateFilePath":"Hooks/state.json"}}
            """);
        CapturingExecutor executor = new();
        string statePath = Path.Combine(fixture.Root, "Hooks", "state.json");

        int absentExit = await RunAsync(selection, executor);

        Assert.Equal(2, absentExit);
        Assert.Equal(0, executor.ExecutionCount);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Hooks")));
        FileHookStateStore store = new();
        HookIdentity identity = new(fixture.Root, "codex", "session-42", "hook");
        await using (IHookStateSession seed = await store.OpenAsync(statePath, identity, TestContext.Current.CancellationToken))
        {
            await seed.SaveAsync(2, TestContext.Current.CancellationToken);
        }

        byte[] before = File.ReadAllBytes(statePath);
        using StringReader replay = new(EventJson(fixture.Root));
        Console.SetIn(replay);
        int existingExit = await RunAsync(selection, executor);

        Assert.Equal(2, existingExit);
        Assert.Equal(0, executor.ExecutionCount);
        Assert.Equal(before, File.ReadAllBytes(statePath));
        Assert.Contains("HookRunner:", console.Error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Appelle l'hôte avec un exécuteur sentinelle remplaçant les processus externes.</summary>
    /// <param name="selection">Valeur brute de la sélection CLI.</param>
    /// <param name="executor">Adaptateur observant les invocations isolées.</param>
    /// <returns>Code de sortie public.</returns>
    private static Task<int> RunAsync(string selection, CapturingExecutor executor)
        => HookRunnerHost.RunAsync(["execute", "hook", "--agent", "codex", "--projects", selection], services =>
        {
            services.AddSingleton<HookStrategy>(new ConsumerHook());
            services.AddSingleton<IIsolatedHookExecutor>(executor);
        }, cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>Écrit des projets référencés, absents, non référencés et non C#.</summary>
    /// <param name="root">Racine temporaire du scénario.</param>
    private static void WriteProjects(string root)
    {
        foreach (string name in new[] { "Api.csproj", "Worker.csproj", "Unreferenced.csproj", "VisualBasic.vbproj" })
        {
            File.WriteAllText(Path.Combine(root, name), "<Project />");
        }

        File.WriteAllText(Path.Combine(root, "Example.slnx"), """
            <Solution>
              <Project Path="Api.csproj" />
              <Project Path="Worker.csproj" />
              <Project Path="Missing.csproj" />
              <Project Path="VisualBasic.vbproj" />
            </Solution>
            """);
    }

    /// <summary>Événement minimal dont l'identité est constante pour les assertions de compteur.</summary>
    /// <param name="root">Dossier de travail fourni par l'agent.</param>
    /// <returns>Entrée native Codex valide.</returns>
    private static string EventJson(string root) => JsonSerializer.Serialize(new
    {
        session_id = "session-42",
        hook_event_name = "Stop",
        cwd = root,
    });
}
