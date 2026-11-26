namespace HookRunner.Application;

/// <summary>Verdict explicite dont les constructions réservent le diagnostic aux échecs qui le publient.</summary>
internal sealed class InvocationOutcome
{
    /// <summary>Construit un verdict cohérent à travers les fabriques de cette classe.</summary>
    /// <param name="kind">Nature du verdict supervisé.</param>
    /// <param name="diagnostic">Diagnostic éventuel d'une correction demandée ou d'un échec technique.</param>
    private InvocationOutcome(InvocationOutcomeKind kind, string? diagnostic = null)
    {
        Kind = kind;
        Diagnostic = diagnostic;
    }

    /// <summary>Nature du verdict supervisé.</summary>
    public InvocationOutcomeKind Kind { get; }

    /// <summary>Diagnostic éventuel à publier pour une correction demandée ou un échec technique.</summary>
    public string? Diagnostic { get; }

    /// <summary>Succès dont la remise à zéro du compteur est sauvegardée.</summary>
    public static InvocationOutcome Success { get; } = new(InvocationOutcomeKind.Success);

    /// <summary>Demande de correction sans blocage parce que le plafond est atteint.</summary>
    public static InvocationOutcome CorrectionLimitReached { get; } = new(InvocationOutcomeKind.CorrectionLimitReached);

    /// <summary>Timeout sans exécution isolée encore active, y compris si aucun hook n'a démarré.</summary>
    public static InvocationOutcome ConfirmedTimeout { get; } = new(InvocationOutcomeKind.ConfirmedTimeout);

    /// <summary>Construit une demande de correction autorisée par le compteur sauvegardé.</summary>
    /// <param name="diagnostic">Consignes éventuelles retournées par le hook.</param>
    /// <returns>Le verdict bloquant de la demande de correction.</returns>
    public static InvocationOutcome CorrectionRequested(string? diagnostic)
        => new(InvocationOutcomeKind.CorrectionRequested, diagnostic);

    /// <summary>Conserve l'échec technique retourné par le hook sans le confondre avec une exception de supervision.</summary>
    /// <param name="diagnostic">Diagnostic éventuel retourné par le hook.</param>
    /// <returns>Le verdict technique du hook isolé.</returns>
    public static InvocationOutcome TechnicalFailure(string? diagnostic)
        => new(InvocationOutcomeKind.TechnicalFailure, diagnostic);
}
