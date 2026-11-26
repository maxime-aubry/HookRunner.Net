namespace HookRunner.Application;

/// <summary>Traduit le résultat supervisé en diagnostic et en code de sortie.</summary>
/// <param name="diagnostics">Flux recevant les verdicts du hook et les erreurs techniques.</param>
internal sealed class ExecutionReporter(TextWriter diagnostics)
{
    /// <summary>Écrit le verdict explicite de l'invocation et son éventuel diagnostic.</summary>
    /// <param name="request">Invocation préparée fournissant le nom du hook et son budget.</param>
    /// <param name="outcome">Verdict établi après supervision et sauvegarde éventuelle du compteur.</param>
    /// <returns>0 pour un succès, un plafond atteint ou un timeout confirmé ; 2 pour les autres échecs.</returns>
    public int Report(IsolatedHookRequest request, InvocationOutcome outcome)
    {
        if (outcome.Kind == InvocationOutcomeKind.ConfirmedTimeout)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' timed out after {request.Options.TimeoutSeconds} seconds; termination confirmed.");
            return 0;
        }

        if (outcome.Kind == InvocationOutcomeKind.Success)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' succeeded.");
            return 0;
        }

        if (outcome.Kind == InvocationOutcomeKind.CorrectionLimitReached)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' reached its correction request limit.");
            return 0;
        }

        if (outcome.Kind == InvocationOutcomeKind.CorrectionRequested)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' requested corrections.");
        }

        diagnostics.WriteLine(outcome.Diagnostic);
        return 2;
    }

    /// <summary>Écrit le message d'une erreur de supervision comme diagnostic technique.</summary>
    /// <param name="exception">Erreur interceptée par le contexte, annulation extérieure comprise.</param>
    /// <returns>Le code de sortie technique 2.</returns>
    public int ReportFailure(Exception exception)
    {
        diagnostics.WriteLine($"Hook execution failed: {exception.Message}");
        return 2;
    }
}
