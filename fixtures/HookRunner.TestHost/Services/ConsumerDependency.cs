namespace HookRunner.TestHost;

/// <summary>
/// Service du consommateur utilisé pour vérifier la reconstruction des dépendances dans l'enfant.
/// </summary>
public sealed class ConsumerDependency
{
    /// <summary>Valeur fixe identifiant le service propre au consommateur.</summary>
    public string Value { get; } = "consumer-service";
}
