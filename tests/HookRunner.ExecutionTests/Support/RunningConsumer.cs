namespace HookRunner.ExecutionTests;

/// <summary>Possède un superviseur concurrent et garantit son nettoyage avant celui de la solution temporaire.</summary>
internal sealed class RunningConsumer : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    /// <summary>Lance un consommateur dont cet objet devient propriétaire.</summary>
    /// <param name="host">Programme compilé à lancer.</param>
    /// <param name="arguments">Commande publique à exécuter.</param>
    /// <param name="json">Événement natif à fournir sur stdin.</param>
    /// <param name="simulation">Comportement déterministe du consommateur, ou null pour son comportement normal.</param>
    public RunningConsumer(ConsumerHost host, string[] arguments, string json, HostSimulation? simulation = null)
    {
        Execution = host.RunCancellableAsync(arguments, json, simulation, cancellation.Token);
    }

    /// <summary>Résultat capturé après la terminaison du superviseur et de son enfant.</summary>
    public Task<HostResult> Execution { get; }

    /// <summary>Annule le consommateur encore actif et attend son nettoyage pendant au plus dix secondes.</summary>
    /// <returns>La fin du nettoyage ; les erreurs autres que l'annulation attendue restent observables.</returns>
    public async ValueTask DisposeAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await WaitForCleanupAsync();
        }
    }

    /// <summary>Observe la terminaison en acceptant seulement l'annulation demandée pour le nettoyage.</summary>
    /// <returns>Une tâche terminée après le nettoyage du processus possédé.</returns>
    private async Task WaitForCleanupAsync()
    {
        try
        {
            await Execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // L'annulation de nettoyage a arrêté et attendu le processus dans ConsumerHost.
            return;
        }
    }
}
