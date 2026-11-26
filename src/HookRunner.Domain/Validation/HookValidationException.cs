namespace HookRunner.Domain;

/// <summary>Signale des corrections métier nécessaires, distinctes d'une panne technique.</summary>
public sealed class HookValidationException : Exception
{
    /// <summary>Construit une demande de correction avec ses diagnostics et consignes.</summary>
    /// <param name="message">Diagnostic métier et consignes de correction.</param>
    public HookValidationException(string message) : base(message) { }

    /// <summary>Construit une demande de correction sans diagnostic spécifique.</summary>
    public HookValidationException() : base("Hook validation requires corrections.") { }

    /// <summary>Conserve la cause de la demande de correction.</summary>
    /// <param name="message">Diagnostic métier et consignes de correction.</param>
    /// <param name="innerException">Cause initiale de la demande de correction.</param>
    public HookValidationException(string message, Exception innerException) : base(message, innerException) { }
}
