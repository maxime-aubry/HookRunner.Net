namespace HookRunner.ExecutionTests;

/// <summary>Observation complète d'un lancement du consommateur de test.</summary>
/// <param name="ProcessId">Identifiant du processus consommateur terminé.</param>
/// <param name="ExitCode">Code de sortie du consommateur.</param>
/// <param name="Output">Contenu complet de sa sortie standard.</param>
/// <param name="Error">Contenu complet de sa sortie d'erreur.</param>
internal sealed record HostResult(int ProcessId, int ExitCode, string Output, string Error);
