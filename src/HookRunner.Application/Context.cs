namespace HookRunner.Application;

public sealed class Context(IIsolatedHookExecutor executor, TextWriter diagnostics)
{
    private IsolatedHookRequest? request;

    public void SetStrategy(HookStrategy strategy, InvocationData invocation, HookOptions options)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        request = new IsolatedHookRequest(strategy.Name, invocation, options);
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsolatedHookRequest execution = request ?? throw new InvalidOperationException("No hook strategy was selected.");
            HookExecutionResult result = await executor.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
            if (result.Status == HookExecutionStatus.Success)
            {
                diagnostics.WriteLine($"Hook '{execution.HookName}' succeeded.");
                return 0;
            }

            diagnostics.WriteLine(result.Diagnostic);
            return 2;
        }
        catch (Exception exception)
        {
            diagnostics.WriteLine($"Hook execution failed: {exception.Message}");
            return 2;
        }
    }
}
