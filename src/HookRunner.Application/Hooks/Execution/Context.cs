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
    private readonly InvocationSupervisor supervisor = new(executor, timeProvider ?? TimeProvider.System);
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
            return await ExecutePreparedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return reporter.ReportFailure(exception);
        }
    }

    private async Task<int> ExecutePreparedAsync(CancellationToken cancellationToken)
    {
        IsolatedHookRequest execution = request ?? throw new InvalidOperationException("No hook strategy was selected.");
        HookExecutionResult? result = await supervisor.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
        return reporter.Report(execution, result);
    }
}
