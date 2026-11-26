using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Rejoue les conflits natifs dans la boucle réelle d'acquisition du verrou.</summary>
public sealed class StateFileLockTests
{
    /// <summary>Un conflit temporaire doit attendre puis remettre le handle réel à l'appelant.</summary>
    /// <param name="errorCode">Code natif Linux, Windows ou macOS.</param>
    /// <returns>Une tâche vérifiant la nouvelle tentative et la propriété du verrou.</returns>
    [Theory]
    [InlineData(11)]
    [InlineData(unchecked((int)0x80070020))]
    [InlineData(unchecked((int)0x80070021))]
    [InlineData(35)]
    public async Task NativeContentionIsRetriedUntilTheFileCanBeOpened(int errorCode)
    {
        using SolutionFixture fixture = new();
        string path = Path.Combine(fixture.Root, "state.lock");
        int attempts = 0;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using FileStream handle = await StateFileLock.AcquireAsync(path, lockPath =>
        {
            if (++attempts == 1)
            {
                throw new IOException("Native lock contention", errorCode);
            }

            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }, deadline.Token);

        Assert.Equal(2, attempts);
        Assert.True(handle.CanWrite);
        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    /// <summary>L'annulation d'une contention persistante arrête l'attente sans nouvel accès au fichier.</summary>
    /// <returns>Une tâche vérifiant l'annulation de l'attente macOS.</returns>
    [Fact]
    public async Task NativeContentionRespectsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        int attempts = 0;
        Task<FileStream> waiting = StateFileLock.AcquireAsync("unused.lock", _ =>
        {
            attempts++;
            cancellation.Cancel();
            throw new IOException("Native lock contention", 35);
        }, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, attempts);
    }

    /// <summary>Les erreurs techniques restent observables et ne deviennent pas des attentes infinies.</summary>
    /// <returns>Une tâche vérifiant la propagation de l'erreur d'ouverture originale.</returns>
    [Fact]
    public async Task OtherIoErrorsArePropagatedWithoutRetry()
    {
        IOException failure = new("Disk failure", 5);
        int attempts = 0;
        Task<FileStream> waiting = StateFileLock.AcquireAsync("unused.lock", _ =>
        {
            attempts++;
            throw failure;
        }, TestContext.Current.CancellationToken);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => waiting));
        Assert.Equal(1, attempts);
    }
}
