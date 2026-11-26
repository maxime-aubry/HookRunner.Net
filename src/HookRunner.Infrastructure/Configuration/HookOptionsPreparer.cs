using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Adaptateur de préparation des options utilisant la configuration et le système de fichiers.</summary>
/// <param name="defaultTimeoutSeconds">Timeout global en secondes utilisé sans surcharge de commande.</param>
/// <param name="stateFilePath">
/// Chemin configuré du fichier d'état, résolu ultérieurement depuis la racine de solution.
/// </param>
public sealed class HookOptionsPreparer(int defaultTimeoutSeconds, string stateFilePath) : IHookOptionsPreparer
{
    /// <summary>
    /// Valide les options puis cherche la première solution ascendante depuis le dossier de l'agent.
    /// </summary>
    /// <param name="args">Arguments de l'invocation, dont l'éventuelle surcharge du timeout.</param>
    /// <param name="invocation">Données de l'événement fournissant le point de départ de la recherche.</param>
    /// <returns>
    /// Les options avec timeout effectif, racine comme dossier par défaut, projets vides et chemin d'état absolu.
    /// </returns>
    /// <remarks>
    /// Le timeout est transmis sans déclencher de minuterie. Aucun fichier d'état n'est créé. Une solution .sln ou
    /// .slnx suffit à sélectionner le dossier courant avant ses parents.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Les arguments ou l'invocation sont null.</exception>
    /// <exception cref="InvalidDataException">
    /// Le timeout n'est pas positif, ou le chemin d'état est vide, enraciné ou sort de la racine de solution.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Une sélection de projets est demandée, fonctionnalité encore non implémentée.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Aucun dossier ascendant ne contient de solution, ou le dossier de départ est introuvable.
    /// </exception>
    public HookOptions Prepare(RunHookArgs args, InvocationData invocation)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(invocation);
        int timeout = ResolveTimeoutSeconds(args.TimeoutSeconds);
        StateFilePathResolver statePath = new(stateFilePath);
        if (args.Projects.Count != 0)
        {
            throw new NotSupportedException("Project selection is not available yet.");
        }

        string root = SolutionRootLocator.Find(invocation.WorkingDirectory);
        return new HookOptions(timeout, root, root, [], statePath.Resolve(root));
    }

    /// <summary>Applique la surcharge de commande au timeout configuré puis valide sa positivité.</summary>
    /// <param name="overrideSeconds">Timeout demandé par la commande ; null conserve la configuration.</param>
    /// <returns>Le budget effectif en secondes, strictement positif.</returns>
    /// <exception cref="InvalidDataException">Le timeout effectif est nul ou négatif.</exception>
    private int ResolveTimeoutSeconds(int? overrideSeconds)
    {
        int timeout = overrideSeconds ?? defaultTimeoutSeconds;
        if (timeout <= 0)
        {
            throw new InvalidDataException("TimeoutSeconds must be a positive integer.");
        }

        return timeout;
    }
}
