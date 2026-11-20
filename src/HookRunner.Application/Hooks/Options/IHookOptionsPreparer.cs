namespace HookRunner.Application;

/// <summary>
/// Frontière de préparation des options entre les arguments applicatifs et l'environnement du consommateur.
/// </summary>
public interface IHookOptionsPreparer
{
    /// <summary>Valide les paramètres de l'invocation et prépare les options destinées au hook.</summary>
    /// <param name="args">Nom du hook, timeout facultatif et noms des projets demandés.</param>
    /// <param name="invocation">
    /// Données de l'événement, dont le dossier de travail servant à rechercher la solution.
    /// </param>
    /// <returns>Les options de cette invocation, avec les chemins résolus par l'adaptateur.</returns>
    /// <remarks>Une erreur de préparation doit être propagée à l'appelant avant le démarrage du hook.</remarks>
    HookOptions Prepare(RunHookArgs args, InvocationData invocation);
}
