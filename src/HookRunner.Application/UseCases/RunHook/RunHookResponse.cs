namespace HookRunner.Application;

/// <summary>Réponse du cas d'usage contenant le code de sortie à restituer au consommateur.</summary>
/// <param name="ExitCode">Code de sortie à restituer au programme appelant.</param>
public sealed record RunHookResponse(int ExitCode) : IResponse;
