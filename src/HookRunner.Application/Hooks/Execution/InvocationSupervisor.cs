namespace HookRunner.Application;

/// <summary>Applique un budget unique et distingue le timeout confirmé des annulations techniques.</summary>
/// <param name="executor">Frontière d'exécution isolée chargée de confirmer l'arrêt du hook.</param>
/// <param name="clock">Horloge monotone utilisée pour mesurer le budget et programmer ses échéances.</param>
internal sealed class InvocationSupervisor(IIsolatedHookExecutor executor, TimeProvider clock)
{
    /// <summary>Retourne null uniquement pour un timeout dont la fin de l'exécution isolée est confirmée.</summary>
    /// <param name="request">Invocation préparée dont les options fixent le budget global.</param>
    /// <param name="cancellationToken">Annulation extérieure, prioritaire sur le timeout.</param>
    /// <returns>Une tâche donnant le résultat reçu à temps, ou null après un timeout confirmé.</returns>
    /// <remarks>Le démarrage et l'exécution partagent une seule échéance ; les erreurs de l'exécuteur remontent.</remarks>
    /// <exception cref="OperationCanceledException">L'appelant a demandé l'annulation.</exception>
    public async Task<HookExecutionResult?> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using InvocationDeadline deadline = new(request.Options.TimeoutSeconds, clock);
        using CancellationTokenSource executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return await AwaitConfirmedExecutionAsync(request, deadline, executionCancellation.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reconnaît un timeout confirmé à partir du token exact renvoyé par l'exécuteur.</summary>
    /// <param name="request">Requête transmise à l'exécuteur isolé.</param>
    /// <param name="deadline">Échéance unique de l'invocation.</param>
    /// <param name="executionToken">Token lié à l'échéance et à l'annulation extérieure.</param>
    /// <param name="cancellationToken">Annulation extérieure qui empêche de conclure à un timeout.</param>
    /// <returns>Une tâche donnant le résultat supervisé, ou null si le timeout et l'arrêt sont confirmés.</returns>
    /// <remarks>Une annulation portant un autre token reste une erreur, même après l'échéance.</remarks>
    private async Task<HookExecutionResult?> AwaitConfirmedExecutionAsync(
        IsolatedHookRequest request,
        InvocationDeadline deadline,
        CancellationToken executionToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAndCheckDeadlineAsync(request, deadline, executionToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            deadline.IsExpired && !cancellationToken.IsCancellationRequested
            && exception.CancellationToken == executionToken)
        {
            // Seul le token exact garantit que l'adaptateur a confirmé l'arrêt.
            return null;
        }
    }

    /// <summary>Attend l'exécuteur puis écarte tout résultat arrivé après l'échéance globale.</summary>
    /// <param name="request">Requête isolée à exécuter.</param>
    /// <param name="deadline">Échéance contrôlée après la terminaison de l'exécution.</param>
    /// <param name="executionToken">Token transmis à l'exécuteur pour demander l'arrêt.</param>
    /// <param name="cancellationToken">Annulation extérieure contrôlée avant de restituer le verdict.</param>
    /// <returns>Une tâche donnant le résultat reçu à temps, ou null si l'échéance est atteinte.</returns>
    /// <exception cref="OperationCanceledException">L'exécuteur ou l'appelant a annulé l'opération.</exception>
    private async Task<HookExecutionResult?> ExecuteAndCheckDeadlineAsync(
        IsolatedHookRequest request,
        InvocationDeadline deadline,
        CancellationToken executionToken,
        CancellationToken cancellationToken)
    {
        HookExecutionResult result = await executor.ExecuteAsync(request, executionToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return deadline.IsExpired ? null : result;
    }
}
