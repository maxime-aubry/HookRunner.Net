using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>
/// Exécuteur isolé utilisant un processus consommateur et des fichiers temporaires de transport.
/// </summary>
/// <param name="command">Commande de relancement du programme consommateur.</param>
public sealed class ProcessHookExecutor(ChildProcessCommand command) : IIsolatedHookExecutor
{
    /// <summary>
    /// Transmet la requête par fichiers temporaires à un processus enfant et valide son résultat versionné.
    /// </summary>
    /// <param name="request">
    /// Requête isolée contenant le nom du hook, l'invocation et les options préparées.
    /// </param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche donnant le statut et le diagnostic neutres reçus de l'enfant.</returns>
    /// <remarks>
    /// Les flux stdout et stderr de l'enfant sont drainés sans être recopiés. Si l'attente est interrompue,
    /// l'arbre du processus est arrêté et sa terminaison attendue au plus cinq secondes. Le dossier de transport
    /// est supprimé dans tous les cas ; une erreur de nettoyage peut se propager. Aucun timeout autonome n'est
    /// déclenché à partir des options.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// L'enfant quitte anormalement, n'écrit pas de résultat, ou produit une version, un statut ou un diagnostic
    /// invalide.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    /// Le résultat enfant n'est pas un JSON désérialisable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// L'exécution ou la confirmation d'arrêt est annulée.
    /// </exception>
    public async Task<HookExecutionResult> ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
    {
        using ChildHookTransport transport = new();
        await transport.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        ChildProcessRunner runner = new(command);
        int exitCode = await runner.RunAsync(request.Options.DefaultWorkingDirectory, transport.RequestPath,
            transport.ResultPath, cancellationToken).ConfigureAwait(false);
        return await transport.ReadResultAsync(exitCode, cancellationToken).ConfigureAwait(false);
    }
}
