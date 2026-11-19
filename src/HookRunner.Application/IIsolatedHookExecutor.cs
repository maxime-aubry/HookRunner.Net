namespace HookRunner.Application;

public sealed record IsolatedHookRequest(string HookName, InvocationData Invocation, HookOptions Options);

public enum HookExecutionStatus
{
    Success,
    ValidationFailure,
    TechnicalFailure,
}

public sealed record HookExecutionResult(HookExecutionStatus Status, string? Diagnostic);

public interface IIsolatedHookExecutor
{
    Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken);
}
