namespace HookRunner.Application;

/// <summary>
/// Options préparées pour une invocation ; leur construction seule n'effectue aucune validation ni entrée-sortie.
/// </summary>
/// <param name="TimeoutSeconds">
/// Timeout effectif en secondes après application de la configuration et de la surcharge.
/// </param>
/// <param name="SolutionRoot">Chemin absolu du premier dossier ascendant contenant une solution.</param>
/// <param name="DefaultWorkingDirectory">Dossier de travail utilisé pour lancer le hook isolé.</param>
/// <param name="Projects">Projets cibles résolus ; liste vide en l'absence de sélection.</param>
/// <param name="StateFilePath">Chemin du fichier d'état ; aucune persistance n'est effectuée par ce type.</param>
public sealed record HookOptions(
    int TimeoutSeconds,
    string SolutionRoot,
    string DefaultWorkingDirectory,
    IReadOnlyList<TargetProject> Projects,
    string StateFilePath);
