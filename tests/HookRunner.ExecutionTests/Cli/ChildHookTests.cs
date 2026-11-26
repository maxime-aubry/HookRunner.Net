using System.Text.Json;
using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.ExecutionTests;

/// <summary>Lecture, exécution et écriture du protocole enfant via l'hôte public.</summary>
[Collection("Console")]
public sealed class ChildHookTests
{
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
}
