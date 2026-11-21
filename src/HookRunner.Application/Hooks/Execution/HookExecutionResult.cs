namespace HookRunner.Application;

/// <summary>Résultat neutre d'exécution, indépendant du protocole interne du processus enfant.</summary>
/// <param name="Status">Statut d'exécution du hook isolé.</param>
/// <param name="Diagnostic">Message associé à l'échec ; peut être null pour un succès.</param>
public sealed record HookExecutionResult(HookExecutionStatus Status, string? Diagnostic);
