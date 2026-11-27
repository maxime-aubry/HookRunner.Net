using System.IO.Pipes;

namespace HookRunner.ExecutionTests;

/// <summary>Synchronise un hook enfant avec le test par un canal nommé, sans attente fondée sur sa vitesse.</summary>
internal sealed class HookGate : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;

    /// <summary>Crée un canal unique avant le lancement du consommateur.</summary>
    public HookGate()
    {
        Name = "hookrunner-" + Guid.NewGuid().ToString("N");
        pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    /// <summary>Nom transmis au hook par la configuration de simulation.</summary>
    public string Name { get; }

    /// <summary>Attend le signal émis depuis le hook, après acquisition du verrou par son superviseur.</summary>
    /// <returns>Une tâche terminée après le signal, bornée par une échéance technique de dix secondes.</returns>
    public async Task WaitForEntryAsync()
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await pipe.WaitForConnectionAsync(timeout.Token);
        byte[] ready = new byte[1];
        await pipe.ReadExactlyAsync(ready, timeout.Token);
        Assert.Equal(1, ready[0]);
    }

    /// <summary>Autorise le hook à terminer : 0 succès, 1 demande métier, 2 erreur technique.</summary>
    /// <param name="outcome">Verdict à produire dans le processus enfant.</param>
    /// <returns>Une tâche terminée après transmission du verdict.</returns>
    public Task ReleaseAsync(byte outcome)
        => pipe.WriteAsync(new byte[] { outcome }, TestContext.Current.CancellationToken).AsTask();

    /// <summary>Ferme le canal et interrompt un éventuel échange encore actif.</summary>
    /// <returns>La fin de la libération du canal.</returns>
    public ValueTask DisposeAsync() => pipe.DisposeAsync();
}
