namespace HookRunner.Application;

/// <summary>Traduit le résultat supervisé en diagnostic et en code de sortie.</summary>
internal sealed class ExecutionReporter(TextWriter diagnostics)
{
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

    public int ReportFailure(Exception exception)
    {
        diagnostics.WriteLine($"Hook execution failed: {exception.Message}");
        return 2;
    }
}
