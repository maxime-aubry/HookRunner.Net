namespace HookRunner.Application;

/// <summary>Applique un budget unique et distingue le timeout confirmé des annulations techniques.</summary>
internal sealed class InvocationSupervisor(IIsolatedHookExecutor executor, TimeProvider clock)
{
    /// <summary>Retourne null uniquement pour un timeout dont la fin de l'exécution isolée est confirmée.</summary>
    public async Task<HookExecutionResult?> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using InvocationDeadline deadline = new(request.Options.TimeoutSeconds, clock);
        using CancellationTokenSource executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return await AwaitConfirmedExecutionAsync(request, deadline, executionCancellation.Token, cancellationToken).ConfigureAwait(false);
    }

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
