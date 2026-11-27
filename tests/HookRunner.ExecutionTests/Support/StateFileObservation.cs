using System.Text.Json;

namespace HookRunner.ExecutionTests;

/// <summary>Lit le chemin publié sans verrou de stockage pendant les écritures d'un scénario.</summary>
internal static class StateFileObservation
{
    /// <summary>Démarre le lecteur avant d'autoriser les écritures et l'attend après leur terminaison.</summary>
    /// <param name="path">Document existant qui doit rester lisible pendant sa publication.</param>
    /// <param name="publish">Écritures autorisées seulement après la première lecture complète.</param>
    /// <param name="assertDocument">Assertions appliquées à chaque document lu, y compris le dernier.</param>
    /// <returns>Une tâche terminée après les écritures et la dernière lecture directe.</returns>
    /// <remarks>Un fichier absent, inaccessible ou incomplet fait échouer le scénario sans nouvelle tentative.</remarks>
    public static async Task VerifyAsync(string path, Func<Task> publish, Action<JsonElement> assertDocument)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task reading = ObserveAsync(path, ready, finished.Task, assertDocument, timeout.Token);
        await PublishAndFinishAsync(publish, ready.Task, finished, reading);
    }

    /// <summary>Libère les écrivains après la première lecture et garantit l'attente du lecteur en cas d'échec.</summary>
    /// <param name="publish">Écritures du scénario, dont les ressources restent possédées par l'appelant.</param>
    /// <param name="ready">Signal de première lecture réussie.</param>
    /// <param name="finished">Signal arrêtant la boucle après les écritures.</param>
    /// <param name="reading">Lecteur dont toute erreur doit rester observable.</param>
    /// <returns>Une tâche terminée après les écritures et le nettoyage du lecteur.</returns>
    private static async Task PublishAndFinishAsync(Func<Task> publish, Task ready, TaskCompletionSource finished, Task reading)
    {
        try
        {
            Task first = await Task.WhenAny(ready, reading);
            await first;
            await publish();
        }
        finally
        {
            finished.TrySetResult();
            await reading;
        }
    }

    /// <summary>Échantillonne directement le document jusqu'à la fin des écritures, puis le relit une dernière fois.</summary>
    /// <param name="path">Chemin publié à rouvrir pour chaque lecture.</param>
    /// <param name="ready">Signal émis après la première lecture et ses assertions.</param>
    /// <param name="finished">Signal indiquant que toutes les écritures ont terminé.</param>
    /// <param name="assertDocument">Validation de chaque instantané complet.</param>
    /// <param name="cancellationToken">Échéance technique du lecteur.</param>
    /// <returns>Une tâche en échec dès qu'un état absent ou invalide est observé.</returns>
    private static async Task ObserveAsync(string path, TaskCompletionSource ready, Task finished,
        Action<JsonElement> assertDocument, CancellationToken cancellationToken)
    {
        while (!finished.IsCompleted)
        {
            await ReadAsync(path, assertDocument, cancellationToken);
            ready.TrySetResult();
            await Task.Yield();
        }

        await ReadAsync(path, assertDocument, cancellationToken);
    }

    /// <summary>Valide un instantané sans empêcher le remplacement du fichier sous Windows.</summary>
    /// <param name="path">Chemin public, sans acquisition du verrou de document.</param>
    /// <param name="assertDocument">Assertions exécutées avant de fermer cet instantané.</param>
    /// <param name="cancellationToken">Annulation de l'ouverture et de la lecture.</param>
    /// <returns>Une tâche terminée après lecture, validation et libération du fichier.</returns>
    private static async Task ReadAsync(string path, Action<JsonElement> assertDocument, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        assertDocument(document.RootElement);
    }
}
