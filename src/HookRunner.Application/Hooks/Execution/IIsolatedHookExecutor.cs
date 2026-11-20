namespace HookRunner.Application;

/// <summary>Frontière applicative d'exécution du hook dans un environnement isolé.</summary>
public interface IIsolatedHookExecutor
{
    /// <summary>
    /// Exécute un hook au-delà de la frontière d'isolation et restitue un résultat indépendant du protocole de
    /// transport.
    /// </summary>
    /// <param name="request">
    /// Requête isolée contenant le nom du hook, l'invocation et les options préparées.
    /// </param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche contenant le statut du hook et son éventuel diagnostic.</returns>
    /// <remarks>
    /// Les erreurs de lancement, de transport et d'annulation peuvent être propagées ; le contexte les traduit en
    /// code de sortie technique.
    /// </remarks>
    Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken);
}
