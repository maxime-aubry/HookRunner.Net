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
    /// L'annulation demande l'arrêt forcé immédiat du hook isolé, même si la stratégie ignore son token.
    /// L'adaptateur confirme l'arrêt dans un délai technique fini avant de terminer cette tâche.
    /// Une OperationCanceledException portant exactement le token reçu signifie que l'arrêt est confirmé
    /// (ou qu'aucun enfant n'a démarré). Une confirmation échouée doit lever une autre exception, même si son
    /// délai technique expire. Un résultat retourné garantit également la fin de l'exécution isolée.
    /// Les erreurs de lancement, de transport et de confirmation remontent au contexte comme erreurs techniques.
    /// </remarks>
    Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken);
}
