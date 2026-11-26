using System.Diagnostics;

namespace HookRunner.Infrastructure;

/// <summary>Possède le processus enfant, draine ses flux et confirme son arrêt avant de terminer.</summary>
/// <param name="command">Exécutable du consommateur et arguments initiaux de son lanceur.</param>
internal sealed class ChildProcessRunner(ChildProcessCommand command)
{
    /// <summary>Lance le consommateur en mode enfant et attend sa terminaison avec drainage des flux.</summary>
    /// <param name="workingDirectory">Dossier de travail du processus enfant.</param>
    /// <param name="requestPath">Chemin de la requête JSON préparée.</param>
    /// <param name="resultPath">Chemin où l'enfant doit écrire son résultat.</param>
    /// <param name="cancellationToken">Annulation empêchant le lancement ou demandant l'arrêt de l'enfant.</param>
    /// <returns>Une tâche donnant le code de sortie du processus terminé.</returns>
    /// <remarks>Après le lancement, toute interruption déclenche une tentative d'arrêt de l'arbre des processus.</remarks>
    public async Task<int> RunAsync(string workingDirectory, string requestPath, string resultPath, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = CreateStartInfo(workingDirectory, requestPath, resultPath);
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Process.Start(start)!;
        await RunStartedProcessAsync(process, cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Ajoute les arguments du protocole enfant à la commande du consommateur sans passer par un shell.</summary>
    /// <param name="workingDirectory">Dossier de travail à affecter au processus.</param>
    /// <param name="requestPath">Chemin de la requête transmis comme argument distinct.</param>
    /// <param name="resultPath">Chemin du résultat transmis comme argument distinct.</param>
    /// <returns>Les paramètres de lancement avec les trois flux standard redirigés et sans fenêtre.</returns>
    private ProcessStartInfo CreateStartInfo(string workingDirectory, string requestPath, string resultPath)
    {
        ProcessStartInfo start = new(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(ChildHookProtocol.Command);
        start.ArgumentList.Add(requestPath);
        start.ArgumentList.Add(resultPath);
        return start;
    }

    /// <summary>Attend les flux et le processus en garantissant une tentative de terminaison dans tous les cas.</summary>
    /// <param name="process">Processus enfant déjà démarré.</param>
    /// <param name="cancellationToken">Annulation de l'attente et du drainage.</param>
    /// <returns>Une tâche terminée après confirmation de l'arrêt, ou en échec si celle-ci échoue.</returns>
    private static async Task RunStartedProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await DrainAndWaitAsync(process, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await EnsureTerminatedAsync(process).ConfigureAwait(false);
        }
    }

    /// <summary>Ferme stdin et draine stdout et stderr sans les recopier, puis attend la fin des lectures.</summary>
    /// <param name="process">Enfant dont les flux standard sont redirigés.</param>
    /// <param name="cancellationToken">Annulation commune au drainage et à l'attente du processus.</param>
    /// <returns>Une tâche terminée après la sortie de l'enfant et le drainage des deux flux.</returns>
    private static async Task DrainAndWaitAsync(Process process, CancellationToken cancellationToken)
    {
        process.StandardInput.Close();
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
    }

    /// <summary>Force l'arrêt de l'arbre si l'enfant est encore actif, puis attend sa terminaison au plus cinq secondes.</summary>
    /// <param name="process">Processus enfant dont l'arrêt doit être confirmé.</param>
    /// <returns>Une tâche terminée dès que la sortie de l'enfant est confirmée.</returns>
    /// <exception cref="OperationCanceledException">La confirmation dépasse le délai technique de cinq secondes.</exception>
    private static async Task EnsureTerminatedAsync(Process process)
    {
        if (!process.HasExited)
        {
            using CancellationTokenSource termination = new(TimeSpan.FromSeconds(5));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(termination.Token).ConfigureAwait(false);
        }
    }
}
