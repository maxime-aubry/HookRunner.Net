using System.Text.Json.Serialization;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Enveloppe versionnée du résultat écrit par le processus enfant.</summary>
/// <param name="Version">Version du protocole interne permettant de valider le résultat enfant.</param>
/// <param name="Status">Statut d'exécution du hook ; sa présence dans le JSON est obligatoire.</param>
/// <param name="Diagnostic">Message associé à l'échec ; peut être null pour un succès.</param>
public sealed record ChildHookResult(int Version, [property: JsonRequired] HookExecutionStatus Status, string? Diagnostic);
