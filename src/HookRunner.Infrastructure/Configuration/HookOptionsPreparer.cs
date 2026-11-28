using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Adaptateur de préparation des options utilisant la configuration et le système de fichiers.</summary>
/// <param name="defaultTimeoutSeconds">Timeout global en secondes utilisé sans surcharge de commande.</param>
/// <param name="stateFilePath">
/// Chemin configuré du fichier d'état, résolu ultérieurement depuis la racine de solution.
/// </param>
/// <param name="maxBlockingRequests">Plafond global de demandes de correction.</param>
/// <param name="hookLimits">Surcharges par nom de hook, sans distinction de casse.</param>
public sealed class HookOptionsPreparer(int defaultTimeoutSeconds, string stateFilePath,
    int maxBlockingRequests = 3, IReadOnlyDictionary<string, int>? hookLimits = null) : IHookOptionsPreparer
{
    private readonly Dictionary<string, int> limits = hookLimits is null
        ? new(StringComparer.OrdinalIgnoreCase) : new(hookLimits, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valide les options puis cherche la première solution ascendante depuis le dossier de l'agent.
    /// </summary>
    /// <param name="args">Arguments de l'invocation, dont l'éventuelle surcharge du timeout.</param>
    /// <param name="invocation">Données de l'événement fournissant le point de départ de la recherche.</param>
    /// <returns>
    /// Les options avec timeout effectif, racine comme dossier par défaut, projets résolus et chemin d'état absolu.
    /// </returns>
    /// <remarks>
    /// Le timeout est transmis sans déclencher de minuterie. Aucun fichier d'état n'est créé. Une solution .sln ou
    /// .slnx suffit à sélectionner le dossier courant avant ses parents.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Les arguments ou l'invocation sont null.</exception>
    /// <exception cref="InvalidDataException">
    /// Le timeout, le plafond de demandes de correction, le chemin d'état ou la sélection de projets est invalide.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Aucun dossier ascendant ne contient de solution, ou le dossier de départ est introuvable.
    /// </exception>
    /// <exception cref="Microsoft.Build.Exceptions.InvalidProjectFileException">Une solution .sln est illisible ou mal formée.</exception>
    /// <exception cref="Microsoft.VisualStudio.SolutionPersistence.Model.SolutionException">Une solution .slnx ne respecte pas le format attendu.</exception>
    public HookOptions Prepare(RunHookArgs args, InvocationData invocation)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(invocation);
        int timeoutSeconds = ResolveTimeoutSeconds(args.TimeoutSeconds);
        StateFilePathResolver statePathResolver = new(stateFilePath);
        int blockingRequestLimit = ResolveMaxBlockingRequests(args.HookName);

        string solutionRoot = SolutionRootLocator.Find(invocation.WorkingDirectory);
        IReadOnlyList<TargetProject> projects = TargetProjectResolver.Resolve(solutionRoot, args.Projects);
        string resolvedStateFilePath = statePathResolver.Resolve(solutionRoot);

        return new HookOptions(
            TimeoutSeconds: timeoutSeconds,
            SolutionRoot: solutionRoot,
            DefaultWorkingDirectory: solutionRoot,
            Projects: projects,
            StateFilePath: resolvedStateFilePath,
            MaxBlockingRequests: blockingRequestLimit);
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

    /// <summary>Applique la surcharge du hook au plafond global puis valide sa valeur effective.</summary>
    /// <param name="hookName">Nom du hook recherché sans distinction de casse.</param>
    /// <returns>Le plafond effectif de demandes de correction, supérieur ou égal à zéro.</returns>
    /// <exception cref="InvalidDataException">Le plafond effectif est négatif.</exception>
    private int ResolveMaxBlockingRequests(string hookName)
    {
        int limit = limits.GetValueOrDefault(hookName, maxBlockingRequests);
        if (limit < 0)
        {
            throw new InvalidDataException("MaxBlockingRequests must be non-negative.");
        }

        return limit;
    }
}
