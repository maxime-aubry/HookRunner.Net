using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Double d'exécution mémorisant la requête reçue et renvoyant un succès.</summary>
internal sealed class CapturingExecutor : IIsolatedHookExecutor
{
    /// <summary>
    /// Dernière requête mémorisée par le double d'exécution, ou null avant le premier appel.
    /// </summary>
    public IsolatedHookRequest? Request { get; private set; }

    /// <summary>Nombre d'invocations isolées reçues par cet exécuteur.</summary>
    public int ExecutionCount { get; private set; }

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
        ExecutionCount++;
        return Task.FromResult(new HookExecutionResult(HookExecutionStatus.Success, null));
    }
}
