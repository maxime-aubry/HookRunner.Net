namespace HookRunner.ExecutionTests;

/// <summary>Paramètres de simulation transmis au consommateur par ses variables d'environnement.</summary>
/// <param name="Mode">Nom du comportement à simuler dans le consommateur.</param>
/// <param name="Result">Résultat brut du mode invalid-result ; null conserve la variable héritée.</param>
internal sealed record HostSimulation(string Mode, string? Result);
