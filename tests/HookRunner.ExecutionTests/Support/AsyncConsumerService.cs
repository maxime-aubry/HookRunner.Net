namespace HookRunner.ExecutionTests;

/// <summary>Dépendance jetable permettant d'observer la libération asynchrone du conteneur.</summary>
internal sealed class AsyncConsumerService : IAsyncDisposable
{
    /// <summary>Indique si la dépendance de test doit échouer lors de sa libération.</summary>
    public bool FailsOnDispose { get; init; }
    /// <summary>Indique si la libération de la dépendance a été exécutée.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Simule une libération asynchrone et mémorise son exécution, même lorsqu'elle échoue.</summary>
    /// <returns>Une ValueTask qui termine normalement ou lève l'erreur de libération simulée.</returns>
    /// <exception cref="InvalidOperationException">
    /// FailsOnDispose demande une erreur de libération.
    /// </exception>
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Disposed = true;
        if (FailsOnDispose)
        {
            throw new InvalidOperationException("consumer disposal failure");
        }
    }
}
