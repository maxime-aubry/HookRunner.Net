namespace HookRunner.Application;

/// <summary>Arguments typés d'exécution avant résolution de l'environnement et des projets.</summary>
/// <param name="HookName">Nom du hook sélectionné ; canonique après résolution de la stratégie.</param>
/// <param name="TimeoutSeconds">
/// Timeout en secondes ; null dans les arguments si aucune surcharge n'est demandée.
/// </param>
/// <param name="Projects">Noms des projets demandés ; liste vide en l'absence de sélection.</param>
public sealed record RunHookArgs(string HookName, int? TimeoutSeconds, IReadOnlyList<string> Projects);
