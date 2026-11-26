using HookRunner.Domain;

namespace HookRunner.Application;

/// <summary>Applique un budget unique et distingue le timeout confirmé des annulations techniques.</summary>
/// <param name="executor">Frontière d'exécution isolée chargée de confirmer l'arrêt du hook.</param>
/// <param name="stateStore">Stockage du compteur pendant l'invocation.</param>
/// <param name="clock">Horloge monotone utilisée pour mesurer le budget et programmer ses échéances.</param>
internal sealed class InvocationSupervisor(IIsolatedHookExecutor executor, IHookStateStore stateStore, TimeProvider clock)
{
    /// <summary>Retourne le verdict de l'invocation après supervision et sauvegarde éventuelle du compteur.</summary>
    /// <param name="request">Invocation préparée dont les options fixent le budget global.</param>
    /// <param name="cancellationToken">Annulation extérieure, prioritaire sur le timeout.</param>
    /// <returns>Un verdict explicite ; un timeout garantit qu'aucune exécution isolée n'est encore active.</returns>
    /// <remarks>Le démarrage et l'exécution partagent une seule échéance ; les erreurs de l'exécuteur remontent.</remarks>
    /// <exception cref="OperationCanceledException">L'appelant a demandé l'annulation.</exception>
    public async Task<InvocationOutcome> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using InvocationDeadline deadline = new(request.Options.TimeoutSeconds, clock);
        using CancellationTokenSource executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return await ExecuteWithinDeadlineAsync(request, deadline, executionCancellation.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Traduit l'annulation du budget pendant l'ouverture de l'état, l'exécution ou la sauvegarde.</summary>
    /// <param name="request">Requête transmise à l'exécuteur isolé.</param>
    /// <param name="deadline">Échéance unique de l'invocation.</param>
    /// <param name="executionToken">Token lié à l'échéance et à l'annulation extérieure.</param>
    /// <param name="cancellationToken">Annulation extérieure qui empêche de conclure à un timeout.</param>
    /// <returns>Le verdict supervisé, y compris un timeout confirmé si le budget est annulé.</returns>
    /// <remarks>Une annulation portant un autre token reste une erreur, même après l'échéance. Le hook n'a pas
    /// démarré pendant l'ouverture de l'état ; l'exécuteur confirme son arrêt avant la sauvegarde.</remarks>
    private async Task<InvocationOutcome> ExecuteWithinDeadlineAsync(
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
            // Le token exact distingue l'arrêt confirmé par l'exécuteur d'une annulation technique indépendante.
            return InvocationOutcome.ConfirmedTimeout;
        }
    }

    /// <summary>Possède l'état pendant l'exécution et sa sauvegarde, en écartant les résultats tardifs.</summary>
    /// <param name="request">Requête isolée à exécuter.</param>
    /// <param name="deadline">Échéance contrôlée après la terminaison de l'exécution.</param>
    /// <param name="executionToken">Token transmis à l'exécuteur pour demander l'arrêt.</param>
    /// <param name="cancellationToken">Annulation extérieure contrôlée avant de restituer le verdict.</param>
    /// <returns>Le verdict après sauvegarde éventuelle, ou un timeout confirmé si l'échéance est atteinte.</returns>
    /// <exception cref="OperationCanceledException">L'exécuteur ou l'appelant a annulé l'opération.</exception>
    private async Task<InvocationOutcome> ExecuteAndCheckDeadlineAsync(
        IsolatedHookRequest request,
        InvocationDeadline deadline,
        CancellationToken executionToken,
        CancellationToken cancellationToken)
    {
        HookIdentity identity = new(request.Options.SolutionRoot, request.Invocation.Agent,
            request.Invocation.SessionId, request.HookName);
        await using IHookStateSession state = await stateStore.OpenAsync(request.Options.StateFilePath, identity, executionToken).ConfigureAwait(false);
        executionToken.ThrowIfCancellationRequested();
        if (deadline.IsExpired)
        {
            return InvocationOutcome.ConfirmedTimeout;
        }

        HookExecutionResult result = await executor.ExecuteAsync(request, executionToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (deadline.IsExpired)
        {
            return InvocationOutcome.ConfirmedTimeout;
        }

        return await ApplyResultToStateAsync(state, result, request.Options.MaxBlockingRequests, executionToken).ConfigureAwait(false);
    }

    /// <summary>Applique le résultat au compteur et ne publie le verdict qu'après sa sauvegarde.</summary>
    /// <param name="state">Session conservée sous verrou par le superviseur.</param>
    /// <param name="result">Résultat reçu avant l'échéance.</param>
    /// <param name="maxBlockingRequests">Plafond des demandes de correction.</param>
    /// <param name="executionToken">Annulation liée au budget et à l'appelant pendant la sauvegarde.</param>
    /// <returns>Le verdict correspondant au résultat et au compteur sauvegardé.</returns>
    private static async Task<InvocationOutcome> ApplyResultToStateAsync(
        IHookStateSession state, HookExecutionResult result, int maxBlockingRequests, CancellationToken executionToken)
    {
        if (result.Status == HookExecutionStatus.Success)
        {
            await state.SaveAsync(0, executionToken).ConfigureAwait(false);
            return InvocationOutcome.Success;
        }

        if (result.Status == HookExecutionStatus.ValidationFailure)
        {
            RequestCounter counter = new(state.Count);
            bool shouldBlock = counter.RequestCorrection(maxBlockingRequests);
            await state.SaveAsync(counter.Count, executionToken).ConfigureAwait(false);
            return shouldBlock ? InvocationOutcome.CorrectionRequested(result.Diagnostic) : InvocationOutcome.CorrectionLimitReached;
        }

        return InvocationOutcome.TechnicalFailure(result.Diagnostic);
    }
}
