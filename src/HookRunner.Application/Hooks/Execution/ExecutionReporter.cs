namespace HookRunner.Application;

/// <summary>Traduit le résultat supervisé en diagnostic et en code de sortie.</summary>
/// <param name="diagnostics">Flux recevant les verdicts du hook et les erreurs techniques.</param>
internal sealed class ExecutionReporter(TextWriter diagnostics)
{
    /// <summary>Écrit le verdict du hook, ou un diagnostic de timeout lorsque le résultat est null.</summary>
    /// <param name="request">Invocation préparée fournissant le nom du hook et son budget.</param>
    /// <param name="result">Résultat de l'exécution ; null représente un timeout avec arrêt confirmé.</param>
    /// <returns>0 pour un succès ou un timeout confirmé ; 2 pour un statut d'échec.</returns>
    public int Report(IsolatedHookRequest request, HookExecutionResult? result)
    {
        if (result is null)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' timed out after {request.Options.TimeoutSeconds} seconds; termination confirmed.");
            return 0;
        }

        if (result.Status == HookExecutionStatus.Success)
        {
            diagnostics.WriteLine($"Hook '{request.HookName}' succeeded.");
            return 0;
        }

        diagnostics.WriteLine(result.Diagnostic);
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
