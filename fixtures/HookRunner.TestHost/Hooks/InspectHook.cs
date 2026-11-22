using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.TestHost;

/// <summary>
/// Hook consommateur de test permettant d'observer la composition, les données et les erreurs enfant.
/// </summary>
/// <param name="dependency">Service du consommateur dont la valeur sera inscrite dans l'observation.</param>
public sealed class InspectHook(ConsumerDependency dependency) : HookStrategy
{
    /// <summary>Nom du hook ou du projet cible représenté par ce type.</summary>
    public override string Name => "inspect";

    /// <summary>
    /// Observe les données préparées et la dépendance injectée depuis un véritable processus consommateur.
    /// </summary>
    /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
    /// <param name="options">Options préparées pour cette invocation du hook.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche écrivant l'observation JSON au chemin observation_path de l'événement.</returns>
    /// <remarks>
    /// HOOKRUNNER_TEST_MODE peut demander une attente annulable ou un blocage C# ignorant l'annulation avec
    /// publication du PID, une sortie immédiate 23 ou une exception technique. Le parcours normal écrit sur stdout pour vérifier son drainage par le
    /// superviseur.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Le mode technical-failure simule une panne du hook.</exception>
    public override async Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(invocation.OriginalJson);
        string output = document.RootElement.GetProperty("observation_path").GetString()!;
        string? mode = Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE");
        await HookFailureSimulation.ApplyAsync(mode, output, cancellationToken);
        string json = JsonSerializer.Serialize(new Observation(Environment.ProcessId, dependency.Value, invocation, options));
        await File.WriteAllTextAsync(output, json, cancellationToken);
        Console.WriteLine("A consumer hook can write arbitrary stdout.");
    }
}
