namespace HookRunner.Application;

public abstract class HookStrategy
{
    public abstract string Name { get; }

    public abstract Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken);
}
