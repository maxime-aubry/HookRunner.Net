namespace HookRunner.Domain;

/// <summary>Signale des corrections métier nécessaires, distinctes d'une panne technique.</summary>
public sealed class HookValidationException : Exception
{
    /// <summary>Construit une demande de correction avec ses diagnostics et consignes.</summary>
    /// <param name="message">Diagnostic métier et consignes ; un message vide utilise le diagnostic par défaut.</param>
    public HookValidationException(string message) : base(NormalizeMessage(message)) { }

    /// <summary>Construit une demande de correction sans diagnostic spécifique.</summary>
    public HookValidationException() : this(string.Empty) { }

    /// <summary>Conserve la cause de la demande de correction.</summary>
    /// <param name="message">Diagnostic métier et consignes ; un message vide utilise le diagnostic par défaut.</param>
    /// <param name="innerException">Cause initiale de la demande de correction.</param>
    public HookValidationException(string message, Exception innerException) : base(NormalizeMessage(message), innerException) { }

    /// <summary>Garantit un diagnostic exploitable sans modifier les consignes explicites du consommateur.</summary>
    /// <param name="message">Consignes fournies par le consommateur, éventuellement vides ou composées d'espaces.</param>
    /// <returns>Le message original s'il contient du texte, sinon le diagnostic métier par défaut.</returns>
    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Hook validation requires corrections.";
        }

        return message;
    }
}
