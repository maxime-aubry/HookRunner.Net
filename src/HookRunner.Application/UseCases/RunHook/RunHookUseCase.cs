namespace HookRunner.Application;

/// <summary>Orchestre la préparation, la sélection et l'exécution isolée d'un seul hook.</summary>
/// <param name="strategies">Stratégies enregistrées par le consommateur, à valider et sélectionner.</param>
/// <param name="optionsPreparer">Adaptateur chargé de valider et résoudre les options d'invocation.</param>
/// <param name="context">Contexte propre à l'invocation, chargé de coordonner son exécution.</param>
public sealed class RunHookUseCase(
    IEnumerable<HookStrategy> strategies,
    IHookOptionsPreparer optionsPreparer,
    Context context) : IUseCase<RunHookRequest, RunHookResponse>
{
    /// <summary>Prépare les options, sélectionne le hook et délègue son exécution au contexte.</summary>
    /// <param name="request">Arguments du hook et données neutres de l'événement déclencheur.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche donnant le code de sortie produit par le contexte dans une réponse typée.</returns>
    /// <remarks>
    /// La préparation et la sélection sont synchrones et leurs erreurs se propagent avant tout lancement. Le
    /// superviseur n'appelle jamais directement la stratégie concrète.
    /// </remarks>
    /// <exception cref="ArgumentNullException">La requête est null.</exception>
    public async Task<RunHookResponse> ExecuteAsync(RunHookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HookOptions options = optionsPreparer.Prepare(request.Args, request.Invocation);
        HookStrategy strategy = HookSelection.Select(strategies, request.Args.HookName);
        context.SetStrategy(strategy, request.Invocation, options);
        return new RunHookResponse(await context.ExecuteAsync(cancellationToken).ConfigureAwait(false));
    }
}
