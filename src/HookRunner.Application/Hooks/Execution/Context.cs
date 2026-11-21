namespace HookRunner.Application;

/// <summary>
/// Coordonne une invocation préparée en déléguant l'exécution du hook à la frontière d'isolation.
/// </summary>
/// <param name="executor">Service d'exécution isolée utilisé pour lancer le hook.</param>
/// <param name="diagnostics">Flux recevant les traces et diagnostics d'exécution.</param>
public sealed class Context(IIsolatedHookExecutor executor, TextWriter diagnostics)
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
    /// Une tâche donnant 0 en cas de succès, ou 2 pour un résultat d'échec, une exception ou un contexte non
    /// préparé.
    /// </returns>
    /// <remarks>
    /// Les exceptions, y compris l'annulation extérieure, sont traduites en diagnostic technique. Cette méthode
    /// n'applique pas encore le timeout des options ni les règles de compteur. Le contexte conserve sa requête et
    /// ne doit pas être partagé entre exécutions concurrentes.
    /// </remarks>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsolatedHookRequest execution = request ?? throw new InvalidOperationException("No hook strategy was selected.");
            HookExecutionResult result = await executor.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
            if (result.Status == HookExecutionStatus.Success)
            {
                diagnostics.WriteLine($"Hook '{execution.HookName}' succeeded.");
                return 0;
            }

            diagnostics.WriteLine(result.Diagnostic);
            return 2;
        }
        catch (Exception exception)
        {
            diagnostics.WriteLine($"Hook execution failed: {exception.Message}");
            return 2;
        }
    }
}
