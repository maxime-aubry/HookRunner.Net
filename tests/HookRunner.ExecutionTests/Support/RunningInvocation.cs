namespace HookRunner.ExecutionTests;

/// <summary>Possède l'annulation d'une invocation et attend sa fin même après une assertion en échec.</summary>
internal sealed class RunningInvocation<T> : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;

    /// <summary>Démarre l'invocation avec une source d'annulation liée au test et possédée par cet objet.</summary>
    /// <param name="execute">Opération à lancer avec le token lié.</param>
    /// <param name="cancellationToken">Annulation du scénario propagée à l'invocation.</param>
    public RunningInvocation(Func<CancellationToken, Task<T>> execute, CancellationToken cancellationToken)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Execution = StartAsync(execute);
    }

    public Task<T> Execution { get; }

    /// <summary>Demande l'annulation de l'invocation sans attendre sa terminaison.</summary>
    public void Cancel() => cancellation.Cancel();

    /// <summary>Exécute le callback en capturant aussi ses exceptions synchrones dans la tâche possédée.</summary>
    /// <param name="execute">Opération recevant le token d'annulation de l'invocation.</param>
    /// <returns>Une tâche donnant le résultat du callback ou portant son exception.</returns>
    private async Task<T> StartAsync(Func<CancellationToken, Task<T>> execute)
        => await execute(cancellation.Token);

    /// <summary>Demande l'annulation, attend la fin pendant au plus dix secondes et libère la source d'annulation.</summary>
    /// <returns>Une opération terminée après l'invocation, propageant son éventuel échec.</returns>
    /// <exception cref="TimeoutException">L'invocation ne termine pas dans les dix secondes suivant la demande.</exception>
    public async ValueTask DisposeAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await Execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }
}
