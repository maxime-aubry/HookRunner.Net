using HookRunner.Application;

namespace HookRunner.TestHost;

/// <summary>Observation sérialisée par le hook consommateur pour les assertions des tests d'intégration.</summary>
/// <param name="ProcessId">Identifiant du processus observé pendant le scénario d'intégration.</param>
/// <param name="Dependency">Valeur du service injecté dans le hook consommateur.</param>
/// <param name="Invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
/// <param name="Options">Options préparées pour cette invocation du hook.</param>
public sealed record Observation(int ProcessId, string Dependency, InvocationData Invocation, HookOptions Options);
