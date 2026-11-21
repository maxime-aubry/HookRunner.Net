namespace HookRunner.Application;

/// <summary>Requête transportable contenant le nom canonique du hook et les données préparées.</summary>
/// <param name="HookName">Nom du hook sélectionné ; canonique après résolution de la stratégie.</param>
/// <param name="Invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
/// <param name="Options">Options préparées pour cette invocation du hook.</param>
public sealed record IsolatedHookRequest(string HookName, InvocationData Invocation, HookOptions Options);
