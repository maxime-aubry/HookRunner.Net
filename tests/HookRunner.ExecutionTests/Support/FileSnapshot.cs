using System.Diagnostics;

namespace HookRunner.ExecutionTests;

/// <summary>Conserve une référence au document publié pour distinguer remplacement et réécriture sur place.</summary>
internal static class FileSnapshot
{
    /// <summary>Crée un lien physique vers le fichier courant, sans garder de handle ouvert pendant sa publication.</summary>
    /// <param name="path">Nouveau nom d'instantané, dans la solution temporaire possédée par le test.</param>
    /// <param name="target">Fichier publié existant sur le même volume.</param>
    /// <param name="cancellationToken">Annulation du test et de la commande native de création du lien.</param>
    /// <returns>Une tâche terminée après création du lien, dont la solution temporaire assurera le nettoyage.</returns>
    /// <exception cref="IOException">La création native du lien échoue.</exception>
    public static async Task CreateHardLinkAsync(string path, string target, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "fsutil.exe";
            foreach (string argument in new[] { "hardlink", "create", path, target })
            {
                start.ArgumentList.Add(argument);
            }
        }
        else
        {
            start.FileName = "ln";
            start.ArgumentList.Add(target);
            start.ArgumentList.Add(path);
        }

        using Process process = Process.Start(start)!;
        await WaitForLinkAsync(process, cancellationToken);
    }

    /// <summary>Capture les diagnostics natifs et nettoie la commande même après annulation.</summary>
    /// <param name="process">Commande de création de lien déjà lancée.</param>
    /// <param name="cancellationToken">Annulation extérieure du scénario.</param>
    /// <returns>Une tâche terminée après création ou arrêt et nettoyage de la commande.</returns>
    private static async Task WaitForLinkAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string diagnostic = await output + await error;
            if (process.ExitCode != 0)
            {
                throw new IOException($"Hard link creation failed: {diagnostic}");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }
}
