namespace HookRunner.Application;

/// <summary>Échéance monotone unique, y compris pour les durées dépassant la capacité d'une minuterie système.</summary>
internal sealed class InvocationDeadline : IAsyncDisposable
{
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly TimeProvider clock;
    private readonly TimeSpan budget;
    private readonly long started;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ITimer timer;

    /// <summary>Démarre le budget avant toute opération d'exécution isolée.</summary>
    /// <param name="timeoutSeconds">Durée strictement positive représentable par Int32.</param>
    /// <param name="clock">Horloge monotone et fabrique de minuteries.</param>
    /// <exception cref="ArgumentOutOfRangeException">La durée n'est pas positive.</exception>
    public InvocationDeadline(int timeoutSeconds, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSeconds);
        this.clock = clock;
        budget = TimeSpan.FromSeconds(timeoutSeconds);
        started = clock.GetTimestamp();
        timer = clock.CreateTimer(_ => CheckDeadline(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        CheckDeadline();
    }

    /// <summary>Token annulé à l'échéance globale.</summary>
    public CancellationToken Token => cancellation.Token;

    /// <summary>Échéance atteinte, même si l'ordonnanceur n'a pas encore exécuté le rappel de minuterie.</summary>
    public bool IsExpired => clock.GetElapsedTime(started) >= budget;

    /// <summary>Annule à l'échéance ou programme uniquement le budget restant.</summary>
    private void CheckDeadline()
    {
        TimeSpan remaining = budget - clock.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero)
        {
            cancellation.Cancel();
        }
        else
        {
            timer.Change(remaining < MaximumTimerDelay ? remaining : MaximumTimerDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Attend la fin des rappels avant de libérer leur source d'annulation.</summary>
    /// <returns>La fin de la libération des ressources de supervision.</returns>
    public async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync().ConfigureAwait(false);
        cancellation.Dispose();
    }
}
