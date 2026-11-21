namespace HookRunner.ExecutionTests;

/// <summary>Horloge monotone avancée explicitement, avec minuteries déclenchées sans attente réelle.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private long timestamp;

    /// <summary>Fréquence des horodatages exprimés en ticks de TimeSpan.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Lit l'instant monotone contrôlé par le test.</summary>
    /// <returns>L'instant en ticks.</returns>
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);

    /// <summary>Installe une minuterie contrôlée par Advance.</summary>
    /// <param name="callback">Action à l'échéance.</param>
    /// <param name="state">État transmis à l'action.</param>
    /// <param name="dueTime">Délai initial.</param>
    /// <param name="period">Période, ou InfiniteTimeSpan pour un déclenchement unique.</param>
    /// <returns>La minuterie enregistrée.</returns>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (timers)
        {
            ManualTimer timer = new(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
    }

    /// <summary>Avance le temps et déclenche les minuteries arrivées à échéance.</summary>
    /// <param name="elapsed">Durée ajoutée à l'horloge.</param>
    /// <param name="dispatchTimers">False pour simuler un retard d'ordonnancement des rappels.</param>
    public void Advance(TimeSpan elapsed, bool dispatchTimers = true)
    {
        Interlocked.Add(ref timestamp, elapsed.Ticks);
        if (!dispatchTimers)
        {
            return;
        }

        List<ManualTimer> due;
        lock (timers)
        {
            due = [.. timers.Where(timer => timer.Due <= timestamp)];
            foreach (ManualTimer timer in due)
            {
                timer.Due = long.MaxValue;
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    /// <summary>Minuterie à déclenchement unique utilisée par l'horloge contrôlée.</summary>
    /// <param name="clock">Horloge propriétaire.</param>
    /// <param name="callback">Action à déclencher.</param>
    /// <param name="state">État de l'action.</param>
    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; } = long.MaxValue;

        /// <summary>Planifie l'échéance unique ; les périodes sont refusées pour éviter une simulation trompeuse.</summary>
        /// <param name="dueTime">Délai avant déclenchement.</param>
        /// <param name="period">Doit être InfiniteTimeSpan.</param>
        /// <returns>True une fois l'échéance modifiée.</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            lock (clock.timers)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.GetTimestamp() + dueTime.Ticks;
            }

            return true;
        }

        /// <summary>Exécute le rappel à l'échéance.</summary>
        public void Fire() => callback(state);

        /// <summary>Retire la minuterie des échéances à déclencher.</summary>
        public void Dispose()
        {
            lock (clock.timers)
            {
                clock.timers.Remove(this);
            }
        }

        /// <summary>Retire la minuterie sans attente asynchrone.</summary>
        /// <returns>Une opération terminée.</returns>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
