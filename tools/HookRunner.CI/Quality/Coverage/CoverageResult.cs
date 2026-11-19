namespace HookRunner.CI;

/// <summary>Verdict de couverture d'une assembly et inventaire des lignes et branches non couvertes.</summary>
/// <param name="Assembly">Nom de l'assembly évaluée par le contrôle de couverture.</param>
/// <param name="Status">Verdict du contrôle : passed, failed ou not-applicable.</param>
/// <param name="UncoveredLines">Identifiants triés des lignes sans exécution observée.</param>
/// <param name="UncoveredBranches">Identifiants triés des branches sans exécution observée.</param>
public sealed record CoverageResult(string Assembly, string Status, string[] UncoveredLines, string[] UncoveredBranches);
