namespace HookRunner.Application;

/// <summary>Requête complète du cas d'usage d'exécution d'un hook.</summary>
/// <param name="Args">Arguments typés de la commande d'exécution.</param>
/// <param name="Invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
public sealed record RunHookRequest(RunHookArgs Args, InvocationData Invocation) : IRequest;
