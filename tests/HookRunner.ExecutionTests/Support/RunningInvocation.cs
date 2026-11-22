namespace HookRunner.ExecutionTests;

/// <summary>Possède l'annulation d'une invocation et attend sa fin même après une assertion en échec.</summary>
internal sealed class RunningInvocation<T> : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;

    public RunningInvocation(Func<CancellationToken, Task<T>> execute, CancellationToken cancellationToken)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Execution = StartAsync(execute);
    }

    public Task<T> Execution { get; }

    public void Cancel() => cancellation.Cancel();

    private async Task<T> StartAsync(Func<CancellationToken, Task<T>> execute)
        // Capture aussi une exception synchrone afin que DisposeAsync possède toujours le nettoyage.
        => await execute(cancellation.Token);

    public async ValueTask DisposeAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await Execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }
}
