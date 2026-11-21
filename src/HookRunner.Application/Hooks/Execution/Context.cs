namespace HookRunner.Application;

/// <summary>
/// Coordonne une invocation préparée en déléguant l'exécution du hook à la frontière d'isolation.
/// </summary>
/// <param name="executor">Service d'exécution isolée utilisé pour lancer le hook.</param>
/// <param name="diagnostics">Flux recevant les traces et diagnostics d'exécution.</param>
/// <param name="timeProvider">Horloge de supervision ; null utilise l'horloge système.</param>
public sealed class Context(IIsolatedHookExecutor executor, TextWriter diagnostics, TimeProvider? timeProvider = null)
{
    private IsolatedHookRequest? request;

    /// <summary>
    /// Prépare l'exécution isolée de la stratégie sélectionnée en remplaçant la requête du contexte.
    /// </summary>
    /// <param name="strategy">
    /// Stratégie dont le nom canonique sera transmis à l'enfant ; son instance n'est pas exécutée ici.
    /// </param>
    /// <param name="invocation">Données neutres de l'événement à transmettre au hook.</param>
    /// <param name="options">Options déjà préparées pour cette invocation.</param>
    /// <exception cref="ArgumentNullException">La stratégie est null.</exception>
    public void SetStrategy(HookStrategy strategy, InvocationData invocation, HookOptions options)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        request = new IsolatedHookRequest(strategy.Name, invocation, options);
    }

    /// <summary>Exécute la requête préparée via l'exécuteur isolé et écrit son diagnostic.</summary>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>
    /// Une tâche donnant 0 en cas de succès ou de timeout avec arrêt confirmé, ou 2 pour un échec métier,
    /// technique, une annulation extérieure ou un contexte non préparé.
    /// </returns>
    /// <remarks>
    /// Le budget unique commence avant l'appel de l'exécuteur, qui doit forcer l'arrêt sur annulation et en
    /// confirmer la terminaison dans un délai technique fini. Un résultat tardif est ignoré. Les règles de compteur
    /// ne sont pas encore appliquées. Le contexte ne doit pas être partagé entre exécutions concurrentes.
    /// </remarks>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsolatedHookRequest execution = request ?? throw new InvalidOperationException("No hook strategy was selected.");
            cancellationToken.ThrowIfCancellationRequested();
            int exitCode;
            await using (InvocationDeadline deadline = new(execution.Options.TimeoutSeconds, timeProvider ?? TimeProvider.System))
            {
                exitCode = await ExecuteIsolatedAsync(execution, deadline, cancellationToken).ConfigureAwait(false);
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            diagnostics.WriteLine($"Hook execution failed: {exception.Message}");
            return 2;
        }
    }

    /// <summary>Attend le hook et traduit son résultat sans renouveler l'échéance de supervision.</summary>
    /// <param name="execution">Requête préparée à transmettre à l'enfant.</param>
    /// <param name="deadline">Échéance unique déjà démarrée.</param>
    /// <param name="cancellationToken">Annulation extérieure, prioritaire sur le timeout.</param>
    /// <returns>Le code 0 ou 2 après interprétation du résultat ou de l'arrêt confirmé.</returns>
    private async Task<int> ExecuteIsolatedAsync(IsolatedHookRequest execution, InvocationDeadline deadline, CancellationToken cancellationToken)
    {
        using CancellationTokenSource executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        HookExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(execution, executionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            deadline.IsExpired && !cancellationToken.IsCancellationRequested
            && exception.CancellationToken == executionCancellation.Token)
        {
            return TraceTimeout(execution);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (deadline.IsExpired)
        {
            return TraceTimeout(execution);
        }

        if (result.Status == HookExecutionStatus.Success)
        {
            diagnostics.WriteLine($"Hook '{execution.HookName}' succeeded.");
            return 0;
        }

        diagnostics.WriteLine(result.Diagnostic);
        return 2;
    }

    /// <summary>Émet uniquement la trace d'un timeout dont l'arrêt a été confirmé par l'exécuteur.</summary>
    /// <param name="execution">Invocation dont le résultat métier doit être ignoré.</param>
    /// <returns>Le code 0 autorisant la poursuite sans succès métier implicite.</returns>
    private int TraceTimeout(IsolatedHookRequest execution)
    {
        diagnostics.WriteLine($"Hook '{execution.HookName}' timed out after {execution.Options.TimeoutSeconds} seconds; termination confirmed.");
        return 0;
    }
}
