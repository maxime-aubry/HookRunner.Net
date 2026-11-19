namespace HookRunner.Application;

public sealed record RunHookArgs(string HookName, int? TimeoutSeconds, IReadOnlyList<string> Projects);

public sealed record RunHookRequest(RunHookArgs Args, InvocationData Invocation) : IRequest;

public sealed record RunHookResponse(int ExitCode) : IResponse;

public sealed class RunHookUseCase(
    IEnumerable<HookStrategy> strategies,
    IHookOptionsPreparer optionsPreparer,
    Context context) : IUseCase<RunHookRequest, RunHookResponse>
{
    public async Task<RunHookResponse> ExecuteAsync(RunHookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HookOptions options = optionsPreparer.Prepare(request.Args, request.Invocation);
        HookStrategy strategy = HookSelection.Select(strategies, request.Args.HookName);
        context.SetStrategy(strategy, request.Invocation, options);
        return new RunHookResponse(await context.ExecuteAsync(cancellationToken).ConfigureAwait(false));
    }
}
