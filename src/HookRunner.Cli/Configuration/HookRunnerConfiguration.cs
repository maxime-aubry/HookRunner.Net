namespace HookRunner.Cli;

/// <summary>Valeurs globales chargées à côté du programme compilé avant préparation d'une invocation.</summary>
/// <param name="TimeoutSeconds">
/// Timeout global positif en secondes, chargé depuis la configuration ou fixé à 120.
/// </param>
/// <param name="StateFilePath">
/// Chemin d'état encore non résolu, validé lors de la préparation ; aucune persistance n'est effectuée ici.
/// </param>
/// <param name="MaxBlockingRequests">Plafond global de demandes.</param>
/// <param name="HookLimits">Surcharges de plafond par nom de hook.</param>
internal sealed record HookRunnerConfiguration(int TimeoutSeconds, string StateFilePath,
    int MaxBlockingRequests, IReadOnlyDictionary<string, int> HookLimits);
