namespace HookRunner.Application;

/// <summary>
/// Coordonne une invocation préparée en déléguant l'exécution du hook à la frontière d'isolation.
/// </summary>
/// <param name="executor">Service d'exécution isolée utilisé pour lancer le hook.</param>
/// <param name="diagnostics">Flux recevant les traces et diagnostics d'exécution.</param>
/// <param name="stateStore">Stockage persistant des compteurs.</param>
/// <param name="timeProvider">Horloge de supervision ; null utilise l'horloge système.</param>
public sealed class Context(IIsolatedHookExecutor executor, TextWriter diagnostics, IHookStateStore stateStore, TimeProvider? timeProvider = null)
{
    private IsolatedHookRequest? request;
    private readonly InvocationSupervisor supervisor = new(executor, stateStore, timeProvider ?? TimeProvider.System);
    private readonly ExecutionReporter reporter = new(diagnostics);

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
    /// Une tâche donnant 0 en cas de succès, plafond atteint ou timeout avec arrêt confirmé, ou 2 pour une demande métier,
    /// technique, une annulation extérieure ou un contexte non préparé.
    /// </returns>
    /// <remarks>
    /// Le budget unique commence avant l'appel de l'exécuteur, qui doit forcer l'arrêt sur annulation et en
    /// confirmer la terminaison dans un délai technique fini. Un résultat tardif est ignoré. Le compteur est
    /// persistant et seule une fin normale le remet à zéro. Le contexte ne doit pas être partagé entre exécutions concurrentes.
    /// </remarks>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ExecutePreparedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return reporter.ReportFailure(exception);
        }
    }

    /// <summary>Supervise la requête sélectionnée puis écrit le diagnostic correspondant au résultat.</summary>
    /// <param name="cancellationToken">Annulation extérieure, distincte de l'échéance du hook.</param>
    /// <returns>Une tâche donnant 0 pour un succès ou un timeout confirmé, sinon 2.</returns>
    /// <exception cref="InvalidOperationException">Aucune stratégie n'a été préparée dans ce contexte.</exception>
    private async Task<int> ExecutePreparedAsync(CancellationToken cancellationToken)
    {
        IsolatedHookRequest execution = request ?? throw new InvalidOperationException("No hook strategy was selected.");
        (HookExecutionResult? result, bool shouldBlock) = await supervisor.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
        return reporter.Report(execution, result, shouldBlock);
    }
}
