using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

/// <summary>Libération des dépendances du superviseur et de l'enfant, y compris en cas d'échec.</summary>
[Collection("Console")]
public sealed class HostDisposalTests
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
