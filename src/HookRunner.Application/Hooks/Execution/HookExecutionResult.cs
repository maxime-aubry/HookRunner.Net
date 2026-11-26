using HookRunner.Domain;

namespace HookRunner.Application;

/// <summary>Résultat neutre d'exécution, indépendant du protocole interne du processus enfant.</summary>
/// <param name="Status">Statut d'exécution du hook isolé.</param>
/// <param name="Diagnostic">Message associé à l'échec ; peut être null pour un succès.</param>
public sealed record HookExecutionResult(HookExecutionStatus Status, string? Diagnostic)
{
    /// <summary>Traduit une exception de stratégie sans confondre correction et panne technique.</summary>
    /// <param name="exception">Exception levée par la stratégie isolée.</param>
    /// <returns>Statut neutre et diagnostic conservant le message complet.</returns>
    /// <exception cref="ArgumentNullException">L'exception est null.</exception>
    public static HookExecutionResult FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new HookExecutionResult(exception is HookValidationException
            ? HookExecutionStatus.ValidationFailure : HookExecutionStatus.TechnicalFailure, exception.Message);
    }
}
